namespace BlockiumLauncher.Application.Abstractions.Instances;

public enum InstanceContentCategory
{
    Mods = 0,
    ResourcePacks = 1,
    Shaders = 2,
    Worlds = 3,
    Screenshots = 4
}

public enum ContentOriginProvider
{
    Unknown = 0,
    Modrinth = 1,
    CurseForge = 2,
    FileImport = 3,
    Prism = 4,
    MultiMc = 5,
    Local = 6
}

public sealed class ContentSourceMetadata
{
    public ContentOriginProvider Provider { get; init; }
    public string ContentType { get; init; } = string.Empty;
    public string? ProjectId { get; init; }
    public string? VersionId { get; init; }
    public string? FileId { get; init; }
    public string? IconUrl { get; init; }
    public string? OriginalUrl { get; init; }
    public DateTimeOffset? AcquiredAtUtc { get; init; }
}

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

public sealed class InstanceFileMetadata
{
    public string Name { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string AbsolutePath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTimeOffset? LastModifiedAtUtc { get; init; }
    public bool IsDisabled { get; init; }
    public string? IconUrl { get; init; }
    public ContentSourceMetadata? Source { get; init; }
}

public sealed class InstanceServerMetadata
{
    public string RelativePath { get; init; } = string.Empty;
    public string AbsolutePath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTimeOffset? LastModifiedAtUtc { get; init; }
}
