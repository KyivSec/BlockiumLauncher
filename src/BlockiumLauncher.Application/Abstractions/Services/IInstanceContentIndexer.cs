using BlockiumLauncher.Application.Abstractions.Instances;

namespace BlockiumLauncher.Application.Abstractions.Services;

public interface IInstanceContentIndexer
{
    Task<InstanceContentMetadata> ScanAsync(
        string installLocation,
        InstanceContentMetadata? existingMetadata = null,
        CancellationToken cancellationToken = default);
}
