using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Diagnostics;
using BlockiumLauncher.Application.Abstractions.Launch;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Diagnostics;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Launch;

public sealed class LaunchInstanceUseCase
{
    private readonly BuildLaunchPlanUseCase BuildLaunchPlanUseCase;
    private readonly ILaunchProcessRunner LaunchProcessRunner;
    private readonly IInstanceRepository InstanceRepository;
    private readonly IStructuredLogger Logger;
    private readonly IOperationContextFactory OperationContextFactory;

    public LaunchInstanceUseCase(
        BuildLaunchPlanUseCase BuildLaunchPlanUseCase,
        ILaunchProcessRunner LaunchProcessRunner,
        IInstanceRepository InstanceRepository)
        : this(
            BuildLaunchPlanUseCase,
            LaunchProcessRunner,
            InstanceRepository,
            NullStructuredLogger.Instance,
            DefaultOperationContextFactory.Instance)
    {
    }

    public LaunchInstanceUseCase(
        BuildLaunchPlanUseCase BuildLaunchPlanUseCase,
        ILaunchProcessRunner LaunchProcessRunner,
        IInstanceRepository InstanceRepository,
        IStructuredLogger Logger,
        IOperationContextFactory OperationContextFactory)
    {
        this.BuildLaunchPlanUseCase = BuildLaunchPlanUseCase ?? throw new ArgumentNullException(nameof(BuildLaunchPlanUseCase));
        this.LaunchProcessRunner = LaunchProcessRunner ?? throw new ArgumentNullException(nameof(LaunchProcessRunner));
        this.InstanceRepository = InstanceRepository ?? throw new ArgumentNullException(nameof(InstanceRepository));
        this.Logger = Logger ?? throw new ArgumentNullException(nameof(Logger));
        this.OperationContextFactory = OperationContextFactory ?? throw new ArgumentNullException(nameof(OperationContextFactory));
    }

    public async Task<Result<LaunchInstanceResult>> ExecuteAsync(
        LaunchInstanceRequest Request,
        CancellationToken CancellationToken = default)
    {
        var Context = OperationContextFactory.Create("LaunchInstance");

        if (Request is null)
        {
            Logger.Warning(Context, nameof(LaunchInstanceUseCase), "InvalidRequest", "Launch request was null.");
            return Result<LaunchInstanceResult>.Failure(LaunchErrors.InvalidRequest);
        }

        var Instance = await InstanceRepository.GetByIdAsync(Request.InstanceId, CancellationToken).ConfigureAwait(false);
        if (Instance is null)
        {
            Logger.Warning(Context, nameof(LaunchInstanceUseCase), "InstanceNotFound", "Launch request referenced a missing instance.", new
            {
                InstanceId = Request.InstanceId.ToString()
            });

            return Result<LaunchInstanceResult>.Failure(LaunchErrors.InstanceNotFound);
        }

        if (string.IsNullOrWhiteSpace(Instance.InstallLocation) || !Directory.Exists(Instance.InstallLocation))
        {
            await InstanceRepository.DeleteAsync(Instance.InstanceId, CancellationToken).ConfigureAwait(false);

            Logger.Warning(Context, nameof(LaunchInstanceUseCase), "StaleInstanceDeleted", "Launch request referenced stale instance metadata. Metadata was removed.", new
            {
                InstanceId = Instance.InstanceId.ToString(),
                Instance.Name,
                Instance.InstallLocation
            });

            return Result<LaunchInstanceResult>.Failure(LaunchErrors.InstanceDirectoryMissing);
        }

        Logger.Info(Context, nameof(LaunchInstanceUseCase), "LaunchStarted", "Launch workflow started.", new
        {
            InstanceId = Request.InstanceId.ToString(),
            AccountId = Request.AccountId?.ToString(),
            Request.JavaExecutablePath,
            Request.MainClass
        });

        var PlanResult = await BuildLaunchPlanUseCase.ExecuteAsync(
            new BuildLaunchPlanRequest
            {
                InstanceId = Request.InstanceId,
                AccountId = Request.AccountId,
                JavaExecutablePath = Request.JavaExecutablePath,
                MainClass = Request.MainClass,
                AssetsDirectory = Request.AssetsDirectory,
                AssetIndexId = Request.AssetIndexId,
                ClasspathEntries = Request.ClasspathEntries,
                IsDryRun = false
            },
            CancellationToken).ConfigureAwait(false);

        if (PlanResult.IsFailure)
        {
            Logger.Warning(Context, nameof(LaunchInstanceUseCase), "LaunchPlanFailed", "Launch plan build failed during launch.", new
            {
                PlanResult.Error.Code,
                PlanResult.Error.Message
            });

            return Result<LaunchInstanceResult>.Failure(PlanResult.Error);
        }

        var StartResult = await LaunchProcessRunner.StartAsync(PlanResult.Value, CancellationToken).ConfigureAwait(false);
        if (StartResult.IsFailure)
        {
            Logger.Warning(Context, nameof(LaunchInstanceUseCase), "ProcessStartFailed", "Launch process start failed.", new
            {
                StartResult.Error.Code,
                StartResult.Error.Message
            });

            return Result<LaunchInstanceResult>.Failure(StartResult.Error);
        }

        Logger.Info(Context, nameof(LaunchInstanceUseCase), "LaunchProcessStarted", "Launch process started.", new
        {
            StartResult.Value.LaunchId,
            StartResult.Value.ProcessId,
            StartResult.Value.InstanceId
        });

        return StartResult;
    }
}