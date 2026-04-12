using BlockiumLauncher.Application.Abstractions.Launch;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Launch;

public sealed class GetLatestLaunchOutputUseCase
{
    private readonly ILaunchProcessRunner launchProcessRunner;

    public GetLatestLaunchOutputUseCase(ILaunchProcessRunner launchProcessRunner)
    {
        this.launchProcessRunner = launchProcessRunner ?? throw new ArgumentNullException(nameof(launchProcessRunner));
    }

    public async Task<Result<IReadOnlyList<LaunchOutputLine>>> ExecuteAsync(
        GetLatestLaunchOutputRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.InstanceId == default)
        {
            return Result<IReadOnlyList<LaunchOutputLine>>.Failure(LaunchErrors.InvalidRequest);
        }

        return await launchProcessRunner
            .GetLatestOutputAsync(request.InstanceId.ToString(), cancellationToken)
            .ConfigureAwait(false);
    }
}

public sealed class ClearLatestLaunchOutputUseCase
{
    private readonly ILaunchProcessRunner launchProcessRunner;

    public ClearLatestLaunchOutputUseCase(ILaunchProcessRunner launchProcessRunner)
    {
        this.launchProcessRunner = launchProcessRunner ?? throw new ArgumentNullException(nameof(launchProcessRunner));
    }

    public async Task<Result> ExecuteAsync(
        ClearLatestLaunchOutputRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.InstanceId == default)
        {
            return Result.Failure(LaunchErrors.InvalidRequest);
        }

        return await launchProcessRunner
            .ClearLatestOutputAsync(request.InstanceId.ToString(), cancellationToken)
            .ConfigureAwait(false);
    }
}
