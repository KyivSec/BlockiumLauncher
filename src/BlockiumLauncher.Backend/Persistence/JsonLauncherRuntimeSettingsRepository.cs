using BlockiumLauncher.Application.Abstractions.Paths;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Infrastructure.Persistence.Json;

namespace BlockiumLauncher.Infrastructure.Persistence.Repositories;

public sealed class JsonLauncherRuntimeSettingsRepository : ILauncherRuntimeSettingsRepository
{
    private readonly JsonFileStore jsonFileStore;
    private readonly ILauncherPaths launcherPaths;

    public JsonLauncherRuntimeSettingsRepository(JsonFileStore jsonFileStore, ILauncherPaths launcherPaths)
    {
        this.jsonFileStore = jsonFileStore ?? throw new ArgumentNullException(nameof(jsonFileStore));
        this.launcherPaths = launcherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
    }

    public async Task<LauncherRuntimeSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await jsonFileStore
            .ReadOptionalAsync<LauncherRuntimeSettings>(GetSettingsPath(), cancellationToken)
            .ConfigureAwait(false);

        return (settings ?? LauncherRuntimeSettings.CreateDefault()).Normalize();
    }

    public Task SaveAsync(LauncherRuntimeSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return jsonFileStore.WriteAsync(GetSettingsPath(), settings.Normalize(), cancellationToken);
    }

    private string GetSettingsPath()
    {
        return Path.Combine(launcherPaths.DataDirectory, "launcher-runtime-settings.json");
    }
}
