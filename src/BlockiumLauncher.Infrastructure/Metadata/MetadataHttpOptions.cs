namespace BlockiumLauncher.Infrastructure.Metadata;

public sealed class MetadataHttpOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxAttempts { get; init; } = 3;
    public TimeSpan FirstRetryDelay { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan SecondRetryDelay { get; init; } = TimeSpan.FromMilliseconds(750);
    public TimeSpan ThirdRetryDelay { get; init; } = TimeSpan.FromMilliseconds(1500);
}
