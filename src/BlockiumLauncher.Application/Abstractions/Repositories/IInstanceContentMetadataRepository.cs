using BlockiumLauncher.Application.Abstractions.Instances;

namespace BlockiumLauncher.Application.Abstractions.Repositories;

public interface IInstanceContentMetadataRepository
{
    Task<InstanceContentMetadata?> LoadAsync(string installLocation, CancellationToken cancellationToken = default);
    Task SaveAsync(string installLocation, InstanceContentMetadata metadata, CancellationToken cancellationToken = default);
}
