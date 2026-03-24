namespace BlockiumLauncher.Application.Abstractions.Launch;

public interface IRuntimeMetadataStore
{
    Task<RuntimeMetadata?> LoadAsync(string workingDirectory, CancellationToken cancellationToken = default);
}
