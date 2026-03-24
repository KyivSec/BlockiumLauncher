namespace BlockiumLauncher.Application.Abstractions.Instances;

public sealed class InstanceServerMetadata
{
    public string RelativePath { get; init; } = string.Empty;
    public string AbsolutePath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTimeOffset? LastModifiedAtUtc { get; init; }
}
