using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Repositories;
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

    public InstallInstanceUseCase(
        InstallPlanBuilder InstallPlanBuilder,
        ITempWorkspaceFactory TempWorkspaceFactory,
        IInstanceContentInstaller InstanceContentInstaller,
        IFileTransaction FileTransaction,
        IInstanceRepository InstanceRepository)
    {
        this.InstallPlanBuilder = InstallPlanBuilder ?? throw new ArgumentNullException(nameof(InstallPlanBuilder));
        this.TempWorkspaceFactory = TempWorkspaceFactory ?? throw new ArgumentNullException(nameof(TempWorkspaceFactory));
        this.InstanceContentInstaller = InstanceContentInstaller ?? throw new ArgumentNullException(nameof(InstanceContentInstaller));
        this.FileTransaction = FileTransaction ?? throw new ArgumentNullException(nameof(FileTransaction));
        this.InstanceRepository = InstanceRepository ?? throw new ArgumentNullException(nameof(InstanceRepository));
    }

    public async Task<Result<InstallInstanceResult>> ExecuteAsync(
        InstallInstanceRequest Request,
        CancellationToken CancellationToken = default)
    {
        ITempWorkspace? Workspace = null;
        var TransactionStarted = false;

        try
        {
            var PlanResult = await InstallPlanBuilder.BuildAsync(Request, CancellationToken).ConfigureAwait(false);
            if (PlanResult.IsFailure)
            {
                return Result<InstallInstanceResult>.Failure(PlanResult.Error);
            }

            var ExistingInstance = await InstanceRepository.GetByNameAsync(Request.InstanceName.Trim(), CancellationToken).ConfigureAwait(false);
            if (ExistingInstance is not null && !Request.OverwriteIfExists)
            {
                return Result<InstallInstanceResult>.Failure(InstallErrors.InstanceAlreadyExists);
            }

            var Plan = PlanResult.Value;
            if (Directory.Exists(Plan.TargetDirectory) && !Request.OverwriteIfExists)
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
            VersionId? LoaderVersionId = string.IsNullOrWhiteSpace(Plan.LoaderVersion)
                ? null
                : CreateVersionId(Plan.LoaderVersion);

            var Instance = LauncherInstance.Create(
                InstanceId.New(),
                Request.InstanceName.Trim(),
                GameVersionId,
                Plan.LoaderType,
                LoaderVersionId,
                Plan.TargetDirectory,
                DateTimeOffset.UtcNow,
                LaunchProfile.CreateDefault(),
                null);

            await InstanceRepository.SaveAsync(Instance, CancellationToken).ConfigureAwait(false);

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

    private static VersionId CreateVersionId(string Value)
    {
        var Type = typeof(VersionId);

        var ParseMethod = Type.GetMethod("Parse", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(string) }, null);
        if (ParseMethod is not null)
        {
            return (VersionId)ParseMethod.Invoke(null, new object[] { Value })!;
        }

        var CreateMethod = Type.GetMethod("Create", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(string) }, null);
        if (CreateMethod is not null)
        {
            return (VersionId)CreateMethod.Invoke(null, new object[] { Value })!;
        }

        var Constructor = Type.GetConstructor(new[] { typeof(string) });
        if (Constructor is not null)
        {
            return (VersionId)Constructor.Invoke(new object[] { Value });
        }

        throw new InvalidOperationException("Could not create VersionId from string.");
    }
}