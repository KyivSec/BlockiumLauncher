namespace BlockiumLauncher.Application.Abstractions.Services;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
