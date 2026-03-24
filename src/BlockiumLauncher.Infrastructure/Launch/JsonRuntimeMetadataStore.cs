using System.Text.Json;
using BlockiumLauncher.Application.Abstractions.Launch;

namespace BlockiumLauncher.Infrastructure.Launch;

public sealed class JsonRuntimeMetadataStore : IRuntimeMetadataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<RuntimeMetadata?> LoadAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var runtimePath = Path.Combine(workingDirectory, ".blockium", "runtime.json");
        if (!File.Exists(runtimePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(runtimePath, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<RuntimeMetadata>(json, JsonOptions);
    }
}
