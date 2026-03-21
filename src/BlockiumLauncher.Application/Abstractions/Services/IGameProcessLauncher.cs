using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Services;

public interface IGameProcessLauncher
{
    Task<Result<ProcessLaunchResult>> LaunchAsync(LaunchPlan LaunchPlan, CancellationToken CancellationToken);
    Task<Result> KillAsync(int ProcessId, CancellationToken CancellationToken);
}
