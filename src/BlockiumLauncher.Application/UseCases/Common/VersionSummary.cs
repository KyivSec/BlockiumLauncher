using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Common;

public sealed record VersionSummary(
    VersionId VersionId,
    string DisplayName,
    bool IsRelease,
    DateTimeOffset ReleasedAtUtc);
