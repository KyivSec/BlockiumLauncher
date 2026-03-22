using BlockiumLauncher.Application.Abstractions.Launch;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Launch;

public sealed class GetLaunchStatusUseCase
{
    private readonly ILaunchProcessRunner LaunchProcessRunner;

    public GetLaunchStatusUseCase(ILaunchProcessRunner LaunchProcessRunner)
    {
        this.LaunchProcessRunner = LaunchProcessRunner ?? throw new ArgumentNullException(nameof(LaunchProcessRunner));
    }

    public async Task<Result<LaunchInstanceResult>> ExecuteAsync(
        GetLaunchStatusRequest Request,
        CancellationToken CancellationToken = default)
    {
        if (Request is null || Request.LaunchId == Guid.Empty)
        {
            return Result<LaunchInstanceResult>.Failure(LaunchErrors.InvalidRequest);
        }

        return await LaunchProcessRunner.GetStatusAsync(Request.LaunchId, CancellationToken).ConfigureAwait(false);
    }
}