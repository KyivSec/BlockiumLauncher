using BlockiumLauncher.Application.Abstractions.Launch;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Infrastructure.Launch;

public sealed class InstanceLaunchSessionObserver : ILaunchSessionObserver
{
    private readonly IInstanceRepository instanceRepository;
    private readonly IInstanceContentMetadataService instanceContentMetadataService;

    public InstanceLaunchSessionObserver(
        IInstanceRepository instanceRepository,
        IInstanceContentMetadataService instanceContentMetadataService)
    {
        this.instanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
        this.instanceContentMetadataService = instanceContentMetadataService ?? throw new ArgumentNullException(nameof(instanceContentMetadataService));
    }

    public Task OnStartedAsync(Guid launchId, string instanceId, DateTimeOffset startedAtUtc, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task OnExitedAsync(
        Guid launchId,
        string instanceId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset exitedAtUtc,
        int? exitCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return;
        }

        var instance = await instanceRepository
            .GetByIdAsync(new InstanceId(instanceId), cancellationToken)
            .ConfigureAwait(false);

        if (instance is null)
        {
            return;
        }

        await instanceContentMetadataService
            .RecordLaunchAsync(instance, startedAtUtc, exitedAtUtc, cancellationToken)
            .ConfigureAwait(false);
    }
}
