using BlockiumLauncher.Application.Abstractions.Launch;

namespace BlockiumLauncher.Application.UseCases.Launch;

public sealed class NullRuntimeMetadataStore : IRuntimeMetadataStore
{
    public static NullRuntimeMetadataStore Instance { get; } = new();

    private NullRuntimeMetadataStore()
    {
    }

    public Task<RuntimeMetadata?> LoadAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<RuntimeMetadata?>(null);
    }
}
