using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Domain.Entities;

namespace BlockiumLauncher.Application.Abstractions.Services;

public interface IInstanceContentMetadataService
{
    Task<InstanceContentMetadata?> GetAsync(
        LauncherInstance instance,
        bool reindexIfMissing = false,
        CancellationToken cancellationToken = default);

    Task<InstanceContentMetadata> ReindexAsync(
        LauncherInstance instance,
        CancellationToken cancellationToken = default);

    Task<InstanceContentMetadata> SetModEnabledAsync(
        LauncherInstance instance,
        string modReference,
        bool enabled,
        CancellationToken cancellationToken = default);

    Task<InstanceContentMetadata> RecordLaunchAsync(
        LauncherInstance instance,
        DateTimeOffset startedAtUtc,
        DateTimeOffset exitedAtUtc,
        CancellationToken cancellationToken = default);
}
