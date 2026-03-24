using System;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Diagnostics;
using BlockiumLauncher.Application.Abstractions.Launch;
using BlockiumLauncher.Application.Diagnostics;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Launch;

public sealed class LaunchInstanceUseCase
{
    private readonly BuildLaunchPlanUseCase BuildLaunchPlanUseCase;
    private readonly ILaunchProcessRunner LaunchProcessRunner;
    private readonly IStructuredLogger Logger;
    private readonly IOperationContextFactory OperationContextFactory;

    public LaunchInstanceUseCase(
        BuildLaunchPlanUseCase BuildLaunchPlanUseCase,
        ILaunchProcessRunner LaunchProcessRunner)
        : this(
            BuildLaunchPlanUseCase,
            LaunchProcessRunner,
            NullStructuredLogger.Instance,
            DefaultOperationContextFactory.Instance)
    {
    }

    public LaunchInstanceUseCase(
        BuildLaunchPlanUseCase BuildLaunchPlanUseCase,
        ILaunchProcessRunner LaunchProcessRunner,
        IStructuredLogger Logger,
        IOperationContextFactory OperationContextFactory)
    {
        this.BuildLaunchPlanUseCase = BuildLaunchPlanUseCase ?? throw new ArgumentNullException(nameof(BuildLaunchPlanUseCase));
        this.LaunchProcessRunner = LaunchProcessRunner ?? throw new ArgumentNullException(nameof(LaunchProcessRunner));
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
