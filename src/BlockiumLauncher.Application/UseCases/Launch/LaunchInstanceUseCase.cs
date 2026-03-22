using BlockiumLauncher.Application.Abstractions.Launch;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Launch;

public sealed class LaunchInstanceUseCase
{
    private readonly BuildLaunchPlanUseCase BuildLaunchPlanUseCase;
    private readonly ILaunchProcessRunner LaunchProcessRunner;

    public LaunchInstanceUseCase(
        BuildLaunchPlanUseCase BuildLaunchPlanUseCase,
        ILaunchProcessRunner LaunchProcessRunner)
    {
        this.BuildLaunchPlanUseCase = BuildLaunchPlanUseCase ?? throw new ArgumentNullException(nameof(BuildLaunchPlanUseCase));
        this.LaunchProcessRunner = LaunchProcessRunner ?? throw new ArgumentNullException(nameof(LaunchProcessRunner));
    }

    public async Task<Result<LaunchInstanceResult>> ExecuteAsync(
        LaunchInstanceRequest Request,
        CancellationToken CancellationToken = default)
    {
        if (Request is null)
        {
            return Result<LaunchInstanceResult>.Failure(LaunchErrors.InvalidRequest);
        }

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
            return Result<LaunchInstanceResult>.Failure(PlanResult.Error);
        }

        return await LaunchProcessRunner.StartAsync(PlanResult.Value, CancellationToken).ConfigureAwait(false);
    }
}