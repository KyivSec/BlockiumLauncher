namespace BlockiumLauncher.Infrastructure.Metadata;

public sealed class MetadataCachePolicy
{
    public TimeSpan FreshTtl { get; init; } = TimeSpan.FromHours(6);
    public TimeSpan MaxStaleFallbackAge { get; init; } = TimeSpan.FromDays(7);

    public bool IsFresh(DateTimeOffset LastUpdatedUtc, DateTimeOffset NowUtc)
    {
        return NowUtc - LastUpdatedUtc <= FreshTtl;
    }

    public bool CanUseStaleFallback(DateTimeOffset LastUpdatedUtc, DateTimeOffset NowUtc)
    {
        return NowUtc - LastUpdatedUtc <= MaxStaleFallbackAge;
    }
}
