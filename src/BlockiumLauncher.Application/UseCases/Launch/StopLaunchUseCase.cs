using BlockiumLauncher.Application.Abstractions.Launch;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Launch;

public sealed class StopLaunchUseCase
{
    private readonly ILaunchProcessRunner LaunchProcessRunner;

    public StopLaunchUseCase(ILaunchProcessRunner LaunchProcessRunner)
    {
        this.LaunchProcessRunner = LaunchProcessRunner ?? throw new ArgumentNullException(nameof(LaunchProcessRunner));
    }

    public async Task<Result> ExecuteAsync(
        StopLaunchRequest Request,
        CancellationToken CancellationToken = default)
    {
        if (Request is null || Request.LaunchId == Guid.Empty)
        {
            return Result.Failure(LaunchErrors.InvalidRequest);
        }

        return await LaunchProcessRunner.StopAsync(Request.LaunchId, CancellationToken).ConfigureAwait(false);
    }
}