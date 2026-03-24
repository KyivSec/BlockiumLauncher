using BlockiumLauncher.Application.Abstractions.Launch;

namespace BlockiumLauncher.Application.UseCases.Launch;

public sealed class NullLaunchSessionObserver : ILaunchSessionObserver
{
    public static readonly NullLaunchSessionObserver Instance = new();

    private NullLaunchSessionObserver()
    {
    }

    public Task OnStartedAsync(Guid launchId, string instanceId, DateTimeOffset startedAtUtc, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task OnExitedAsync(Guid launchId, string instanceId, DateTimeOffset startedAtUtc, DateTimeOffset exitedAtUtc, int? exitCode, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
