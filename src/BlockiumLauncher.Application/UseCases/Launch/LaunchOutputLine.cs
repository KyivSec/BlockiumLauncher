namespace BlockiumLauncher.Application.UseCases.Launch;

public sealed class LaunchOutputLine
{
    public DateTimeOffset TimestampUtc { get; init; }
    public string Stream { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}