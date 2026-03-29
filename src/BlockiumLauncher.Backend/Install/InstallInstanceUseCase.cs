using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    public InstallInstanceUseCase(
        InstallPlanBuilder InstallPlanBuilder,
        ITempWorkspaceFactory TempWorkspaceFactory,
        IInstanceContentInstaller InstanceContentInstaller,
        IFileTransaction FileTransaction,
        IInstanceRepository InstanceRepository,
        IInstanceContentMetadataService InstanceContentMetadataService)
    {
        this.InstallPlanBuilder = InstallPlanBuilder ?? throw new ArgumentNullException(nameof(InstallPlanBuilder));
        this.TempWorkspaceFactory = TempWorkspaceFactory ?? throw new ArgumentNullException(nameof(TempWorkspaceFactory));
        this.InstanceContentInstaller = InstanceContentInstaller ?? throw new ArgumentNullException(nameof(InstanceContentInstaller));
        this.FileTransaction = FileTransaction ?? throw new ArgumentNullException(nameof(FileTransaction));
        this.InstanceRepository = InstanceRepository ?? throw new ArgumentNullException(nameof(InstanceRepository));
        this.InstanceContentMetadataService = InstanceContentMetadataService ?? throw new ArgumentNullException(nameof(InstanceContentMetadataService));
    }

    public async Task<Result<InstallInstanceResult>> ExecuteAsync(
        InstallInstanceRequest Request,
        CancellationToken CancellationToken = default)
    {
        ITempWorkspace? Workspace = null;
        var TransactionStarted = false;

        try
        {
            await PruneStaleInstancesAsync(CancellationToken).ConfigureAwait(false);

            var RequestedName = Request.InstanceName.Trim();
            var FinalName = Request.OverwriteIfExists
                ? RequestedName
                : await ResolveAvailableInstanceNameAsync(RequestedName, CancellationToken).ConfigureAwait(false);

            var EffectiveRequest = CloneWithResolvedName(Request, FinalName);

            var PlanResult = await InstallPlanBuilder.BuildAsync(EffectiveRequest, CancellationToken).ConfigureAwait(false);
            if (PlanResult.IsFailure)
            {
                return Result<InstallInstanceResult>.Failure(PlanResult.Error);
            }

            var ExistingInstance = await InstanceRepository.GetByNameAsync(FinalName, CancellationToken).ConfigureAwait(false);
            if (ExistingInstance is not null && !EffectiveRequest.OverwriteIfExists)
            {
                return Result<InstallInstanceResult>.Failure(InstallErrors.InstanceAlreadyExists);
            }

            var Plan = PlanResult.Value;
            if (Directory.Exists(Plan.TargetDirectory) && !EffectiveRequest.OverwriteIfExists)
            {
                return Result<InstallInstanceResult>.Failure(InstallErrors.InstanceAlreadyExists);
            }

            Workspace = await TempWorkspaceFactory.CreateAsync("install", CancellationToken).ConfigureAwait(false);

            var PreparedResult = await InstanceContentInstaller.PrepareAsync(Plan, Workspace, CancellationToken).ConfigureAwait(false);
            if (PreparedResult.IsFailure)
            {
                return Result<InstallInstanceResult>.Failure(PreparedResult.Error);
            }

            var BeginResult = await FileTransaction.BeginAsync(Plan.TargetDirectory, CancellationToken).ConfigureAwait(false);
            if (BeginResult.IsFailure)
            {
                return Result<InstallInstanceResult>.Failure(BeginResult.Error);
            }

            TransactionStarted = true;

            var StageResult = await FileTransaction.StageDirectoryAsync(PreparedResult.Value, CancellationToken).ConfigureAwait(false);
            if (StageResult.IsFailure)
            {
                await FileTransaction.RollbackAsync(CancellationToken).ConfigureAwait(false);
                return Result<InstallInstanceResult>.Failure(StageResult.Error);
            }

            var CommitResult = await FileTransaction.CommitAsync(CancellationToken).ConfigureAwait(false);
            if (CommitResult.IsFailure)
            {
                await FileTransaction.RollbackAsync(CancellationToken).ConfigureAwait(false);
                return Result<InstallInstanceResult>.Failure(CommitResult.Error);
            }

            var GameVersionId = CreateVersionId(Plan.GameVersion);
            VersionId? LoaderVersionId = string.IsNullOrWhiteSpace(Plan.LoaderVersion) ? null : CreateVersionId(Plan.LoaderVersion);

            var Instance = LauncherInstance.Create(
                InstanceId.New(),
                FinalName,
                GameVersionId,
                Plan.LoaderType,
                LoaderVersionId,
                Plan.TargetDirectory,
                DateTimeOffset.UtcNow,
                LaunchProfile.CreateDefault(),
                null);

            await InstanceRepository.SaveAsync(Instance, CancellationToken).ConfigureAwait(false);
            await InstanceContentMetadataService.ReindexAsync(Instance, CancellationToken).ConfigureAwait(false);
            CleanupRedundantInstallArtifacts(Plan.TargetDirectory);

            return Result<InstallInstanceResult>.Success(new InstallInstanceResult
            {
                Instance = Instance,
                InstalledPath = Plan.TargetDirectory
            });
        }
        catch
        {
            if (TransactionStarted)
            {
                await FileTransaction.RollbackAsync(CancellationToken).ConfigureAwait(false);
            }

            return Result<InstallInstanceResult>.Failure(InstallErrors.Unexpected);
        }
        finally
        {
            if (Workspace is not null)
            {
                await Workspace.DisposeAsync().ConfigureAwait(false);
            }

            await FileTransaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task PruneStaleInstancesAsync(CancellationToken CancellationToken)
    {
        var Instances = await InstanceRepository.ListAsync(CancellationToken).ConfigureAwait(false);

        foreach (var Instance in Instances)
        {
            if (!string.IsNullOrWhiteSpace(Instance.InstallLocation) && !Directory.Exists(Instance.InstallLocation))
            {
                await InstanceRepository.DeleteAsync(Instance.InstanceId, CancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<string> ResolveAvailableInstanceNameAsync(string baseName, CancellationToken CancellationToken)
    {
        var Instances = await InstanceRepository.ListAsync(CancellationToken).ConfigureAwait(false);
        var UsedNames = Instances
            .Select(static instance => instance.Name?.Trim())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!UsedNames.Contains(baseName))
        {
            return baseName;
        }

        for (var index = 1; index < int.MaxValue; index++)
        {
            var candidate = $"{baseName} ({index})";
            if (!UsedNames.Contains(candidate))
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
            DownloadRuntime = Request.DownloadRuntime
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
}
