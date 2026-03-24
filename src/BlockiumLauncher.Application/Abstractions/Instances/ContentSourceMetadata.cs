namespace BlockiumLauncher.Application.Abstractions.Instances;

public sealed class ContentSourceMetadata
{
    public ContentOriginProvider Provider { get; init; }
    public string ContentType { get; init; } = string.Empty;
    public string? ProjectId { get; init; }
    public string? VersionId { get; init; }
    public string? FileId { get; init; }
    public string? OriginalUrl { get; init; }
    public DateTimeOffset? AcquiredAtUtc { get; init; }
}
