using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Launch;

public sealed class LaunchInstanceRequest
{
    public InstanceId InstanceId { get; init; }
    public AccountId? AccountId { get; init; }
    public string JavaExecutablePath { get; init; } = string.Empty;
    public string MainClass { get; init; } = string.Empty;
    public string? AssetsDirectory { get; init; }
    public string? AssetIndexId { get; init; }
    public IReadOnlyList<string> ClasspathEntries { get; init; } = [];
}