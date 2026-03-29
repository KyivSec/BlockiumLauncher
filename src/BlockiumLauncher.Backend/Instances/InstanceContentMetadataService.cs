using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Domain.Entities;

namespace BlockiumLauncher.Infrastructure.Services;

public sealed class InstanceContentMetadataService : IInstanceContentMetadataService
{
    private readonly IInstanceContentMetadataRepository repository;
    private readonly IInstanceContentIndexer indexer;

    public InstanceContentMetadataService(
        IInstanceContentMetadataRepository repository,
        IInstanceContentIndexer indexer)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
    }

    public async Task<InstanceContentMetadata?> GetAsync(
        LauncherInstance instance,
        bool reindexIfMissing = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var metadata = await repository.LoadAsync(instance.InstallLocation, cancellationToken).ConfigureAwait(false);
        if (metadata is not null || !reindexIfMissing)
        {
            return metadata;
        }

        return await ReindexAsync(instance, cancellationToken).ConfigureAwait(false);
    }

    public async Task<InstanceContentMetadata> ReindexAsync(
        LauncherInstance instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var existingMetadata = await repository.LoadAsync(instance.InstallLocation, cancellationToken).ConfigureAwait(false);
        var metadata = await indexer
            .ScanAsync(instance.InstallLocation, existingMetadata, cancellationToken)
            .ConfigureAwait(false);

        await repository.SaveAsync(instance.InstallLocation, metadata, cancellationToken).ConfigureAwait(false);
        return metadata;
    }

    public async Task<InstanceContentMetadata> SetModEnabledAsync(
        LauncherInstance instance,
        string modReference,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(modReference);

        var metadata = await GetAsync(instance, reindexIfMissing: true, cancellationToken).ConfigureAwait(false)
            ?? new InstanceContentMetadata();

        var mod = metadata.Mods.FirstOrDefault(item =>
            string.Equals(item.RelativePath, modReference, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Name, modReference, StringComparison.OrdinalIgnoreCase));

        if (mod is null || string.IsNullOrWhiteSpace(mod.AbsolutePath) || !File.Exists(mod.AbsolutePath))
        {
            throw new FileNotFoundException("The requested mod file was not found.", modReference);
        }

        var targetPath = ResolveTargetModPath(mod.AbsolutePath, enabled);
        if (!string.Equals(mod.AbsolutePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(mod.AbsolutePath, targetPath, overwrite: true);
        }

        return await ReindexAsync(instance, cancellationToken).ConfigureAwait(false);
    }

    public async Task<InstanceContentMetadata> RecordLaunchAsync(
        LauncherInstance instance,
        DateTimeOffset startedAtUtc,
        DateTimeOffset exitedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var duration = exitedAtUtc >= startedAtUtc
            ? exitedAtUtc - startedAtUtc
            : TimeSpan.Zero;

        var existingMetadata = await repository.LoadAsync(instance.InstallLocation, cancellationToken).ConfigureAwait(false)
            ?? await indexer.ScanAsync(instance.InstallLocation, null, cancellationToken).ConfigureAwait(false);

        var updatedMetadata = new InstanceContentMetadata
        {
            IndexedAtUtc = existingMetadata.IndexedAtUtc,
            IconPath = existingMetadata.IconPath,
            TotalPlaytimeSeconds = existingMetadata.TotalPlaytimeSeconds + Convert.ToInt64(Math.Max(0, Math.Round(duration.TotalSeconds))),
            LastLaunchAtUtc = startedAtUtc,
            LastLaunchPlaytimeSeconds = Convert.ToInt64(Math.Max(0, Math.Round(duration.TotalSeconds))),
            Mods = existingMetadata.Mods,
            ResourcePacks = existingMetadata.ResourcePacks,
            Shaders = existingMetadata.Shaders,
            Worlds = existingMetadata.Worlds,
            Screenshots = existingMetadata.Screenshots,
            Servers = existingMetadata.Servers
        };

        await repository.SaveAsync(instance.InstallLocation, updatedMetadata, cancellationToken).ConfigureAwait(false);
        return updatedMetadata;
    }

    public async Task<InstanceContentMetadata> ApplySourcesAsync(
        LauncherInstance instance,
        IReadOnlyDictionary<string, ContentSourceMetadata> sourcesByRelativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(sourcesByRelativePath);

        var metadata = await ReindexAsync(instance, cancellationToken).ConfigureAwait(false);
        if (sourcesByRelativePath.Count == 0)
        {
            return metadata;
        }

        var normalized = sourcesByRelativePath
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(
                pair => NormalizeRelativePath(pair.Key),
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);

        var updatedMetadata = new InstanceContentMetadata
        {
            IndexedAtUtc = metadata.IndexedAtUtc,
            IconPath = metadata.IconPath,
            TotalPlaytimeSeconds = metadata.TotalPlaytimeSeconds,
            LastLaunchAtUtc = metadata.LastLaunchAtUtc,
            LastLaunchPlaytimeSeconds = metadata.LastLaunchPlaytimeSeconds,
            Mods = ApplySources(metadata.Mods, normalized),
            ResourcePacks = ApplySources(metadata.ResourcePacks, normalized),
            Shaders = ApplySources(metadata.Shaders, normalized),
            Worlds = ApplySources(metadata.Worlds, normalized),
            Screenshots = ApplySources(metadata.Screenshots, normalized),
            Servers = metadata.Servers
        };

        await repository.SaveAsync(instance.InstallLocation, updatedMetadata, cancellationToken).ConfigureAwait(false);
        return updatedMetadata;
    }

    private static string ResolveTargetModPath(string currentPath, bool enabled)
    {
        if (enabled)
        {
            return currentPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? currentPath[..^".disabled".Length]
                : currentPath;
        }

        if (currentPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
        {
            return currentPath;
        }

        return currentPath + ".disabled";
    }

    private static IReadOnlyList<InstanceFileMetadata> ApplySources(
        IReadOnlyList<InstanceFileMetadata> items,
        IReadOnlyDictionary<string, ContentSourceMetadata> sourcesByRelativePath)
    {
        return items
            .Select(item =>
            {
                if (!sourcesByRelativePath.TryGetValue(NormalizeRelativePath(item.RelativePath), out var source))
                {
                    return item;
                }

                return new InstanceFileMetadata
                {
                    Name = item.Name,
                    RelativePath = item.RelativePath,
                    AbsolutePath = item.AbsolutePath,
                    SizeBytes = item.SizeBytes,
                    LastModifiedAtUtc = item.LastModifiedAtUtc,
                    IsDisabled = item.IsDisabled,
                    Source = source
                };
            })
            .ToList();
    }

    private static string NormalizeRelativePath(string value)
    {
        return value.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
