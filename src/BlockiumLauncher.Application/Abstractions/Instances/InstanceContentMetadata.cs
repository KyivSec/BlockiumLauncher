namespace BlockiumLauncher.Application.Abstractions.Instances;

public sealed class InstanceContentMetadata
{
    public DateTimeOffset IndexedAtUtc { get; init; }
    public string? IconPath { get; init; }
    public long TotalPlaytimeSeconds { get; init; }
    public DateTimeOffset? LastLaunchAtUtc { get; init; }
    public long? LastLaunchPlaytimeSeconds { get; init; }
    public IReadOnlyList<InstanceFileMetadata> Mods { get; init; } = [];
    public IReadOnlyList<InstanceFileMetadata> ResourcePacks { get; init; } = [];
    public IReadOnlyList<InstanceFileMetadata> Shaders { get; init; } = [];
    public IReadOnlyList<InstanceFileMetadata> Worlds { get; init; } = [];
    public IReadOnlyList<InstanceFileMetadata> Screenshots { get; init; } = [];
    public IReadOnlyList<InstanceServerMetadata> Servers { get; init; } = [];
}
