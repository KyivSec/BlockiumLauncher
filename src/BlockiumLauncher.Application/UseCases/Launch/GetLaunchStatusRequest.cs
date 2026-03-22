namespace BlockiumLauncher.Application.UseCases.Launch;

public sealed class GetLaunchStatusRequest
{
    public Guid LaunchId { get; init; }
}