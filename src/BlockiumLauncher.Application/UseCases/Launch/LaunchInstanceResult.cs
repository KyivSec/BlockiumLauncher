using BlockiumLauncher.Contracts.Launch;

namespace BlockiumLauncher.Application.UseCases.Launch;

public sealed class LaunchInstanceResult
{
    public Guid LaunchId { get; init; }
    public string InstanceId { get; init; } = string.Empty;
    public int? ProcessId { get; init; }
    public bool IsRunning { get; init; }
    public bool HasExited { get; init; }
    public int? ExitCode { get; init; }
    public IReadOnlyList<LaunchOutputLine> OutputLines { get; init; } = [];
    public LaunchPlanDto Plan { get; init; } = default!;
}