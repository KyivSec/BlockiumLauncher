using BlockiumLauncher.Application.UseCases.Launch;
using BlockiumLauncher.Contracts.Launch;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Launch;

public interface ILaunchProcessRunner
{
    Task<Result<LaunchInstanceResult>> StartAsync(LaunchPlanDto Plan, CancellationToken CancellationToken = default);
    Task<Result<LaunchInstanceResult>> GetStatusAsync(Guid LaunchId, CancellationToken CancellationToken = default);
    Task<Result> StopAsync(Guid LaunchId, CancellationToken CancellationToken = default);
}