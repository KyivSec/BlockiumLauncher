using System;

namespace BlockiumLauncher.Infrastructure.Persistence.Models;

public sealed class StoredVersionCache
{
    public List<StoredVersionSummary> Versions { get; set; } = [];
}

public sealed class StoredVersionSummary
{
    public string VersionId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsRelease { get; set; }
    public DateTimeOffset ReleasedAtUtc { get; set; }
}