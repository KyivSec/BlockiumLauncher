using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Domain.Entities;

namespace BlockiumLauncher.Application.Diagnostics;

public sealed class NoOpInstanceContentMetadataService : IInstanceContentMetadataService
{
    public static readonly NoOpInstanceContentMetadataService Instance = new();

    private NoOpInstanceContentMetadataService()
    {
    }

    public Task<InstanceContentMetadata?> GetAsync(LauncherInstance instance, bool reindexIfMissing = false, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<InstanceContentMetadata?>(null);
    }

    public Task<InstanceContentMetadata> ReindexAsync(LauncherInstance instance, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new InstanceContentMetadata());
    }

    public Task<InstanceContentMetadata> SetModEnabledAsync(LauncherInstance instance, string modReference, bool enabled, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new InstanceContentMetadata());
    }

    public Task<InstanceContentMetadata> RecordLaunchAsync(LauncherInstance instance, DateTimeOffset startedAtUtc, DateTimeOffset exitedAtUtc, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new InstanceContentMetadata());
    }
}
