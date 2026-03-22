using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Diagnostics;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.Diagnostics;
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
    private readonly IStructuredLogger Logger;
    private readonly IOperationContextFactory OperationContextFactory;

    public InstallInstanceUseCase(
        InstallPlanBuilder InstallPlanBuilder,
        ITempWorkspaceFactory TempWorkspaceFactory,
        IInstanceContentInstaller InstanceContentInstaller,
        IFileTransaction FileTransaction,
        IInstanceRepository InstanceRepository)
        : this(
            InstallPlanBuilder,
            TempWorkspaceFactory,
            InstanceContentInstaller,
            FileTransaction,
            InstanceRepository,
            NullStructuredLogger.Instance,
            DefaultOperationContextFactory.Instance)
    {
    }

    public InstallInstanceUseCase(
        InstallPlanBuilder InstallPlanBuilder,
        ITempWorkspaceFactory TempWorkspaceFactory,
        IInstanceContentInstaller InstanceContentInstaller,
        IFileTransaction FileTransaction,
        IInstanceRepository InstanceRepository,
        IStructuredLogger Logger,
        IOperationContextFactory OperationContextFactory)
    {
        this.InstallPlanBuilder = InstallPlanBuilder ?? throw new ArgumentNullException(nameof(InstallPlanBuilder));
        this.TempWorkspaceFactory = TempWorkspaceFactory ?? throw new ArgumentNullException(nameof(TempWorkspaceFactory));
        this.InstanceContentInstaller = InstanceContentInstaller ?? throw new ArgumentNullException(nameof(InstanceContentInstaller));
        this.FileTransaction = FileTransaction ?? throw new ArgumentNullException(nameof(FileTransaction));
        this.InstanceRepository = InstanceRepository ?? throw new ArgumentNullException(nameof(InstanceRepository));
        this.Logger = Logger ?? throw new ArgumentNullException(nameof(Logger));
        this.OperationContextFactory = OperationContextFactory ?? throw new ArgumentNullException(nameof(OperationContextFactory));
    }

    public async Task<Result<InstallInstanceResult>> ExecuteAsync(
        InstallInstanceRequest Request,
        CancellationToken CancellationToken = default)
    {
        ITempWorkspace? Workspace = null;
        var TransactionStarted = false;
        var Context = OperationContextFactory.Create("InstallInstance");

        Logger.Info(Context, nameof(InstallInstanceUseCase), "InstallStarted", "Install instance workflow started.", new
        {
            Request.InstanceName,
            Request.GameVersion,
            LoaderType = Request.LoaderType.ToString(),
            Request.LoaderVersion,
            Request.TargetDirectory,
            Request.OverwriteIfExists,
            Request.DownloadRuntime
        });

        try
        {
            var PlanResult = await InstallPlanBuilder.BuildAsync(Request, CancellationToken).ConfigureAwait(false);
            if (PlanResult.IsFailure)
            {
                Logger.Warning(Context, nameof(InstallInstanceUseCase), "InstallPlanFailed", "Install plan build failed.", new
                {
                    PlanResult.Error.Code,
                    PlanResult.Error.Message
                });

                return Result<InstallInstanceResult>.Failure(PlanResult.Error);
            }

            var ExistingInstance = await InstanceRepository.GetByNameAsync(Request.InstanceName.Trim(), CancellationToken).ConfigureAwait(false);
            if (ExistingInstance is not null && !Request.OverwriteIfExists)
            {
                Logger.Warning(Context, nameof(InstallInstanceUseCase), "InstanceAlreadyExists", "Instance already exists.", new
                {
                    ExistingInstance.InstanceId,
                    ExistingInstance.Name
                });

                return Result<InstallInstanceResult>.Failure(InstallErrors.InstanceAlreadyExists);
            }

            var Plan = PlanResult.Value;
            if (Directory.Exists(Plan.TargetDirectory) && !Request.OverwriteIfExists)
            {
                Logger.Warning(Context, nameof(InstallInstanceUseCase), "TargetDirectoryExists", "Target directory already exists.", new
                {
                    Plan.TargetDirectory
                });

                return Result<InstallInstanceResult>.Failure(InstallErrors.InstanceAlreadyExists);
            }

            Workspace = await TempWorkspaceFactory.CreateAsync("install", CancellationToken).ConfigureAwait(false);
            Logger.Info(Context, nameof(InstallInstanceUseCase), "WorkspaceCreated", "Temporary workspace created.");

            var PreparedResult = await InstanceContentInstaller.PrepareAsync(Plan, Workspace, CancellationToken).ConfigureAwait(false);
            if (PreparedResult.IsFailure)
            {
                Logger.Warning(Context, nameof(InstallInstanceUseCase), "PrepareFailed", "Instance content preparation failed.", new
                {
                    PreparedResult.Error.Code,
                    PreparedResult.Error.Message
                });

                return Result<InstallInstanceResult>.Failure(PreparedResult.Error);
            }

            var BeginResult = await FileTransaction.BeginAsync(Plan.TargetDirectory, CancellationToken).ConfigureAwait(false);
            if (BeginResult.IsFailure)
            {
                Logger.Warning(Context, nameof(InstallInstanceUseCase), "TransactionBeginFailed", "File transaction begin failed.", new
                {
                    BeginResult.Error.Code,
                    BeginResult.Error.Message
                });

                return Result<InstallInstanceResult>.Failure(BeginResult.Error);
            }

            TransactionStarted = true;

            var StageResult = await FileTransaction.StageDirectoryAsync(PreparedResult.Value, CancellationToken).ConfigureAwait(false);
            if (StageResult.IsFailure)
            {
                await FileTransaction.RollbackAsync(CancellationToken).ConfigureAwait(false);

                Logger.Warning(Context, nameof(InstallInstanceUseCase), "StageDirectoryFailed", "File transaction stage failed.", new
                {
                    StageResult.Error.Code,
                    StageResult.Error.Message
                });

                return Result<InstallInstanceResult>.Failure(StageResult.Error);
            }

            var CommitResult = await FileTransaction.CommitAsync(CancellationToken).ConfigureAwait(false);
            if (CommitResult.IsFailure)
            {
                await FileTransaction.RollbackAsync(CancellationToken).ConfigureAwait(false);

                Logger.Warning(Context, nameof(InstallInstanceUseCase), "CommitFailed", "File transaction commit failed.", new
                {
                    CommitResult.Error.Code,
                    CommitResult.Error.Message
                });

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

            Instance.MarkInstalled();
            await InstanceRepository.SaveAsync(Instance, CancellationToken).ConfigureAwait(false);

            Logger.Info(Context, nameof(InstallInstanceUseCase), "InstallSucceeded", "Install instance workflow succeeded.", new
            {
                InstanceId = Instance.InstanceId.ToString(),
                Instance.Name,
                Plan.TargetDirectory
            });

            return Result<InstallInstanceResult>.Success(new InstallInstanceResult
            {
                Instance = Instance,
                InstalledPath = Plan.TargetDirectory
            });
        }
        catch (Exception Exception)
        {
            if (TransactionStarted)
            {
                await FileTransaction.RollbackAsync(CancellationToken).ConfigureAwait(false);
            }

            Logger.Error(Context, nameof(InstallInstanceUseCase), "InstallUnexpected", "Install instance workflow failed unexpectedly.", new
            {
                Request.InstanceName,
                Request.GameVersion
            }, Exception);

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