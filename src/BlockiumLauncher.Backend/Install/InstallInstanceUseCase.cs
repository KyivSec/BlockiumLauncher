using System.IO;
using System.Linq;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class InstallInstanceUseCase
{
    private readonly InstallPlanBuilder InstallPlanBuilder;
    private readonly ITempWorkspaceFactory TempWorkspaceFactory;
    private readonly IInstanceContentInstaller InstanceContentInstaller;
    private readonly IFileTransaction FileTransaction;
    private readonly IInstanceRepository InstanceRepository;
    private readonly IInstanceContentMetadataService InstanceContentMetadataService;
    private readonly ILauncherRuntimeSettingsRepository LauncherRuntimeSettingsRepository;

    public InstallInstanceUseCase(
        InstallPlanBuilder InstallPlanBuilder,
        ITempWorkspaceFactory TempWorkspaceFactory,
        IInstanceContentInstaller InstanceContentInstaller,
        IFileTransaction FileTransaction,
        IInstanceRepository InstanceRepository,
        IInstanceContentMetadataService InstanceContentMetadataService)
        : this(
            InstallPlanBuilder,
            TempWorkspaceFactory,
            InstanceContentInstaller,
            FileTransaction,
            InstanceRepository,
            InstanceContentMetadataService,
            new DefaultLauncherRuntimeSettingsRepository())
    {
    }

    public InstallInstanceUseCase(
        InstallPlanBuilder InstallPlanBuilder,
        ITempWorkspaceFactory TempWorkspaceFactory,
        IInstanceContentInstaller InstanceContentInstaller,
        IFileTransaction FileTransaction,
        IInstanceRepository InstanceRepository,
        IInstanceContentMetadataService InstanceContentMetadataService,
        ILauncherRuntimeSettingsRepository LauncherRuntimeSettingsRepository)
    {
        this.InstallPlanBuilder = InstallPlanBuilder ?? throw new ArgumentNullException(nameof(InstallPlanBuilder));
        this.TempWorkspaceFactory = TempWorkspaceFactory ?? throw new ArgumentNullException(nameof(TempWorkspaceFactory));
        this.InstanceContentInstaller = InstanceContentInstaller ?? throw new ArgumentNullException(nameof(InstanceContentInstaller));
        this.FileTransaction = FileTransaction ?? throw new ArgumentNullException(nameof(FileTransaction));
        this.InstanceRepository = InstanceRepository ?? throw new ArgumentNullException(nameof(InstanceRepository));
        this.InstanceContentMetadataService = InstanceContentMetadataService ?? throw new ArgumentNullException(nameof(InstanceContentMetadataService));
        this.LauncherRuntimeSettingsRepository = LauncherRuntimeSettingsRepository ?? throw new ArgumentNullException(nameof(LauncherRuntimeSettingsRepository));
    }

    public async Task<Result<InstallInstanceResult>> ExecuteAsync(
        InstallInstanceRequest Request,
        CancellationToken CancellationToken = default)
    {
        var preparedResult = await PrepareAsync(Request, CancellationToken).ConfigureAwait(false);
        if (preparedResult.IsFailure)
        {
            return Result<InstallInstanceResult>.Failure(preparedResult.Error);
        }

        await using var preparedSession = preparedResult.Value;
        return await CommitAsync(preparedSession, CancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<PreparedInstallSession>> PrepareAsync(
        InstallInstanceRequest Request,
        CancellationToken CancellationToken = default)
    {
        ITempWorkspace? workspace = null;

        try
        {
            await PruneStaleInstancesAsync(CancellationToken).ConfigureAwait(false);

            var requestedName = Request.InstanceName.Trim();
            var finalName = Request.OverwriteIfExists
                ? requestedName
                : await ResolveAvailableInstanceNameAsync(requestedName, CancellationToken).ConfigureAwait(false);

            var effectiveRequest = CloneWithResolvedName(Request, finalName);

            var planResult = await InstallPlanBuilder.BuildAsync(effectiveRequest, CancellationToken).ConfigureAwait(false);
            if (planResult.IsFailure)
            {
                return Result<PreparedInstallSession>.Failure(planResult.Error);
            }

            var existingInstance = await InstanceRepository.GetByNameAsync(finalName, CancellationToken).ConfigureAwait(false);
            if (existingInstance is not null && !effectiveRequest.OverwriteIfExists)
            {
                return Result<PreparedInstallSession>.Failure(InstallErrors.InstanceAlreadyExists);
            }

            var plan = planResult.Value;
            if (Directory.Exists(plan.TargetDirectory) && !effectiveRequest.OverwriteIfExists)
            {
                return Result<PreparedInstallSession>.Failure(InstallErrors.InstanceAlreadyExists);
            }

            workspace = await TempWorkspaceFactory.CreateAsync("install", CancellationToken).ConfigureAwait(false);

            var preparedResult = await InstanceContentInstaller
                .PrepareAsync(plan, workspace, Request.PreparationProgress, CancellationToken)
                .ConfigureAwait(false);
            if (preparedResult.IsFailure)
            {
                await workspace.DisposeAsync().ConfigureAwait(false);
                return Result<PreparedInstallSession>.Failure(preparedResult.Error);
            }

            return Result<PreparedInstallSession>.Success(new PreparedInstallSession
            {
                Plan = plan,
                Workspace = workspace,
                PreparedRootPath = preparedResult.Value
            });
        }
        catch
        {
            if (workspace is not null)
            {
                await workspace.DisposeAsync().ConfigureAwait(false);
            }

            return Result<PreparedInstallSession>.Failure(InstallErrors.Unexpected);
        }
    }

    public async Task<Result<InstallInstanceResult>> CommitAsync(
        PreparedInstallSession Session,
        CancellationToken CancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(Session);

        var transactionStarted = false;

        try
        {
            var beginResult = await FileTransaction.BeginAsync(Session.Plan.TargetDirectory, CancellationToken).ConfigureAwait(false);
            if (beginResult.IsFailure)
            {
                return Result<InstallInstanceResult>.Failure(beginResult.Error);
            }

            transactionStarted = true;

            var stageResult = await FileTransaction.StageDirectoryAsync(Session.PreparedRootPath, CancellationToken).ConfigureAwait(false);
            if (stageResult.IsFailure)
            {
                await FileTransaction.RollbackAsync(CancellationToken).ConfigureAwait(false);
                return Result<InstallInstanceResult>.Failure(stageResult.Error);
            }

            var commitResult = await FileTransaction.CommitAsync(CancellationToken).ConfigureAwait(false);
            if (commitResult.IsFailure)
            {
                await FileTransaction.RollbackAsync(CancellationToken).ConfigureAwait(false);
                return Result<InstallInstanceResult>.Failure(commitResult.Error);
            }

            var gameVersionId = CreateVersionId(Session.Plan.GameVersion);
            VersionId? loaderVersionId = string.IsNullOrWhiteSpace(Session.Plan.LoaderVersion)
                ? null
                : CreateVersionId(Session.Plan.LoaderVersion);

            var runtimeSettings = await LauncherRuntimeSettingsRepository.LoadAsync(CancellationToken).ConfigureAwait(false);
            var instance = LauncherInstance.Create(
                InstanceId.New(),
                Session.Plan.InstanceName,
                gameVersionId,
                Session.Plan.LoaderType,
                loaderVersionId,
                Session.Plan.TargetDirectory,
                DateTimeOffset.UtcNow,
                LaunchProfile.CreateDefault(runtimeSettings.DefaultMinMemoryMb, runtimeSettings.DefaultMaxMemoryMb),
                null);

            await InstanceRepository.SaveAsync(instance, CancellationToken).ConfigureAwait(false);
            await InstanceContentMetadataService.ReindexAsync(instance, CancellationToken).ConfigureAwait(false);
            CleanupRedundantInstallArtifacts(Session.Plan.TargetDirectory);

            return Result<InstallInstanceResult>.Success(new InstallInstanceResult
            {
                Instance = instance,
                InstalledPath = Session.Plan.TargetDirectory
            });
        }
        catch
        {
            if (transactionStarted)
            {
                await FileTransaction.RollbackAsync(CancellationToken).ConfigureAwait(false);
            }

            return Result<InstallInstanceResult>.Failure(InstallErrors.Unexpected);
        }
        finally
        {
            await FileTransaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task PruneStaleInstancesAsync(CancellationToken CancellationToken)
    {
        var instances = await InstanceRepository.ListAsync(CancellationToken).ConfigureAwait(false);

        foreach (var instance in instances)
        {
            if (!string.IsNullOrWhiteSpace(instance.InstallLocation) && !Directory.Exists(instance.InstallLocation))
            {
                await InstanceRepository.DeleteAsync(instance.InstanceId, CancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<string> ResolveAvailableInstanceNameAsync(string baseName, CancellationToken CancellationToken)
    {
        var instances = await InstanceRepository.ListAsync(CancellationToken).ConfigureAwait(false);
        var usedNames = instances
            .Select(static instance => instance.Name?.Trim())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!usedNames.Contains(baseName))
        {
            return baseName;
        }

        for (var index = 1; index < int.MaxValue; index++)
        {
            var candidate = $"{baseName} ({index})";
            if (!usedNames.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not resolve a unique instance name.");
    }

    private static InstallInstanceRequest CloneWithResolvedName(InstallInstanceRequest Request, string name)
    {
        return new InstallInstanceRequest
        {
            InstanceName = name,
            GameVersion = Request.GameVersion,
            LoaderType = Request.LoaderType,
            LoaderVersion = Request.LoaderVersion,
            TargetDirectory = Request.TargetDirectory,
            OverwriteIfExists = Request.OverwriteIfExists,
            DownloadRuntime = Request.DownloadRuntime,
            PreparationProgress = Request.PreparationProgress
        };
    }

    private static void CleanupRedundantInstallArtifacts(string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
        {
            return;
        }

        var legacyMinecraftDirectory = Path.Combine(targetDirectory, ".minecraft");
        if (!Directory.Exists(legacyMinecraftDirectory))
        {
            return;
        }

        DeleteDirectoryIfEmpty(Path.Combine(legacyMinecraftDirectory, "config"));
        DeleteDirectoryIfEmpty(Path.Combine(legacyMinecraftDirectory, "mods"));
        DeleteDirectoryIfEmpty(legacyMinecraftDirectory);
    }

    private static void DeleteDirectoryIfEmpty(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        if (Directory.EnumerateFileSystemEntries(path).Any())
        {
            return;
        }

        Directory.Delete(path, recursive: false);
    }

    private static VersionId CreateVersionId(string Value)
    {
        return VersionId.Parse(Value);
    }

    private sealed class DefaultLauncherRuntimeSettingsRepository : ILauncherRuntimeSettingsRepository
    {
        public Task<LauncherRuntimeSettings> LoadAsync(CancellationToken CancellationToken = default)
            => Task.FromResult(LauncherRuntimeSettings.CreateDefault());

        public Task SaveAsync(LauncherRuntimeSettings Settings, CancellationToken CancellationToken = default)
            => Task.CompletedTask;
    }
}
