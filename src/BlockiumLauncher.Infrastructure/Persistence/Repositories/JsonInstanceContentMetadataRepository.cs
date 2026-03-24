using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.Abstractions.Paths;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Infrastructure.Persistence.Json;

namespace BlockiumLauncher.Infrastructure.Persistence.Repositories;

public sealed class JsonInstanceContentMetadataRepository : IInstanceContentMetadataRepository
{
    private readonly ILauncherPaths launcherPaths;
    private readonly JsonFileStore jsonFileStore;

    public JsonInstanceContentMetadataRepository(
        ILauncherPaths launcherPaths,
        JsonFileStore jsonFileStore)
    {
        this.launcherPaths = launcherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
        this.jsonFileStore = jsonFileStore ?? throw new ArgumentNullException(nameof(jsonFileStore));
    }

    public Task<InstanceContentMetadata?> LoadAsync(string installLocation, CancellationToken cancellationToken = default)
    {
        return jsonFileStore.ReadOptionalAsync<InstanceContentMetadata>(
            launcherPaths.GetInstanceMetadataFilePath(installLocation),
            cancellationToken);
    }

    public Task SaveAsync(string installLocation, InstanceContentMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return jsonFileStore.WriteAsync(
            launcherPaths.GetInstanceMetadataFilePath(installLocation),
            metadata,
            cancellationToken);
    }
}
