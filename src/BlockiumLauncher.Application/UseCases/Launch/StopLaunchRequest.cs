namespace BlockiumLauncher.Application.UseCases.Launch;

public sealed class StopLaunchRequest
{
    public Guid LaunchId { get; init; }
}