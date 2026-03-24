namespace BlockiumLauncher.Application.Abstractions.Launch;

public interface ILaunchSessionObserver
{
    Task OnStartedAsync(
        Guid launchId,
        string instanceId,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default);

    Task OnExitedAsync(
        Guid launchId,
        string instanceId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset exitedAtUtc,
        int? exitCode,
        CancellationToken cancellationToken = default);
}
