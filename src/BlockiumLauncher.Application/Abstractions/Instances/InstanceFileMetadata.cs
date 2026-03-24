namespace BlockiumLauncher.Application.Abstractions.Instances;

public sealed class InstanceFileMetadata
{
    public string Name { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string AbsolutePath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTimeOffset? LastModifiedAtUtc { get; init; }
    public bool IsDisabled { get; init; }
    public ContentSourceMetadata? Source { get; init; }
}
