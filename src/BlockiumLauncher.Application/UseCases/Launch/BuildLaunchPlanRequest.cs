using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Launch;

public sealed class BuildLaunchPlanRequest
{
    public InstanceId InstanceId { get; init; }
    public AccountId? AccountId { get; init; }
    public string JavaExecutablePath { get; init; } = string.Empty;
    public string MainClass { get; init; } = string.Empty;
    public string? AssetsDirectory { get; init; }
    public string? AssetIndexId { get; init; }
    public IReadOnlyList<string> ClasspathEntries { get; init; } = [];
    public bool IsDryRun { get; init; } = true;
}