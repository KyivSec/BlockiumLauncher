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
        return await SetContentEnabledAsync(instance, InstanceContentCategory.Mods, modReference, enabled, cancellationToken).ConfigureAwait(false);
    }

    public async Task<InstanceContentMetadata> SetContentEnabledAsync(
        LauncherInstance instance,
        InstanceContentCategory category,
        string contentReference,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentReference);

        var metadata = await GetAsync(instance, reindexIfMissing: true, cancellationToken).ConfigureAwait(false)
            ?? new InstanceContentMetadata();

        var item = ResolveContentItems(metadata, category).FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, contentReference, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Name, contentReference, StringComparison.OrdinalIgnoreCase));

        if (item is null || string.IsNullOrWhiteSpace(item.AbsolutePath) || !File.Exists(item.AbsolutePath))
        {
            throw new FileNotFoundException("The requested content file was not found.", contentReference);
        }

        if (!SupportsDisable(category))
        {
            return await ReindexAsync(instance, cancellationToken).ConfigureAwait(false);
        }

        var targetPath = ResolveTargetContentPath(item.AbsolutePath, enabled);
        if (!string.Equals(item.AbsolutePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(item.AbsolutePath, targetPath, overwrite: true);
        }

        var normalizedSources = CollectSources(metadata);
        if (normalizedSources.Count > 0 &&
            item.Source is not null)
        {
            normalizedSources.Remove(NormalizeRelativePath(item.RelativePath));
            var targetRelativePath = NormalizeRelativePath(Path.GetRelativePath(instance.InstallLocation, targetPath));
            normalizedSources[targetRelativePath] = item.Source;
        }

        var reindexedMetadata = await ReindexAsync(instance, cancellationToken).ConfigureAwait(false);
        if (normalizedSources.Count == 0)
        {
            return reindexedMetadata;
        }

        var updatedMetadata = new InstanceContentMetadata
        {
            IndexedAtUtc = reindexedMetadata.IndexedAtUtc,
            IconPath = reindexedMetadata.IconPath,
            TotalPlaytimeSeconds = reindexedMetadata.TotalPlaytimeSeconds,
            LastLaunchAtUtc = reindexedMetadata.LastLaunchAtUtc,
            LastLaunchPlaytimeSeconds = reindexedMetadata.LastLaunchPlaytimeSeconds,
            Mods = ApplySources(reindexedMetadata.Mods, normalizedSources),
            ResourcePacks = ApplySources(reindexedMetadata.ResourcePacks, normalizedSources),
            Shaders = ApplySources(reindexedMetadata.Shaders, normalizedSources),
            Worlds = ApplySources(reindexedMetadata.Worlds, normalizedSources),
            Screenshots = ApplySources(reindexedMetadata.Screenshots, normalizedSources),
            Servers = reindexedMetadata.Servers
        };

        await repository.SaveAsync(instance.InstallLocation, updatedMetadata, cancellationToken).ConfigureAwait(false);
        return updatedMetadata;
    }

    public async Task<InstanceContentMetadata> DeleteContentAsync(
        LauncherInstance instance,
        InstanceContentCategory category,
        string contentReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentReference);

        var metadata = await GetAsync(instance, reindexIfMissing: true, cancellationToken).ConfigureAwait(false)
            ?? new InstanceContentMetadata();

        var item = ResolveContentItems(metadata, category).FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, contentReference, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Name, contentReference, StringComparison.OrdinalIgnoreCase));

        if (item is null || string.IsNullOrWhiteSpace(item.AbsolutePath) ||
            (!File.Exists(item.AbsolutePath) && !Directory.Exists(item.AbsolutePath)))
        {
            throw new FileNotFoundException("The requested content file was not found.", contentReference);
        }

        if (Directory.Exists(item.AbsolutePath))
        {
            Directory.Delete(item.AbsolutePath, recursive: true);
        }
        else
        {
            File.Delete(item.AbsolutePath);
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

    private static string ResolveTargetContentPath(string currentPath, bool enabled)
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

    private static IReadOnlyList<InstanceFileMetadata> ResolveContentItems(InstanceContentMetadata metadata, InstanceContentCategory category)
    {
        return category switch
        {
            InstanceContentCategory.Mods => metadata.Mods,
            InstanceContentCategory.ResourcePacks => metadata.ResourcePacks,
            InstanceContentCategory.Shaders => metadata.Shaders,
            InstanceContentCategory.Worlds => metadata.Worlds,
            InstanceContentCategory.Screenshots => metadata.Screenshots,
            _ => []
        };
    }

    private static bool SupportsDisable(InstanceContentCategory category)
    {
        return category is InstanceContentCategory.Mods or
            InstanceContentCategory.ResourcePacks or
            InstanceContentCategory.Shaders;
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
                    IconUrl = string.IsNullOrWhiteSpace(source.IconUrl) ? item.IconUrl : source.IconUrl,
                    Source = source
                };
            })
            .ToList();
    }

    private static Dictionary<string, ContentSourceMetadata> CollectSources(InstanceContentMetadata metadata)
    {
        return EnumerateSourceEntries(metadata)
            .Where(static pair => pair.Source is not null)
            .ToDictionary(
                static pair => NormalizeRelativePath(pair.RelativePath),
                static pair => pair.Source!,
                StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<(string RelativePath, ContentSourceMetadata? Source)> EnumerateSourceEntries(InstanceContentMetadata metadata)
    {
        foreach (var item in metadata.Mods)
        {
            yield return (item.RelativePath, item.Source);
        }

        foreach (var item in metadata.ResourcePacks)
        {
            yield return (item.RelativePath, item.Source);
        }

        foreach (var item in metadata.Shaders)
        {
            yield return (item.RelativePath, item.Source);
        }

        foreach (var item in metadata.Worlds)
        {
            yield return (item.RelativePath, item.Source);
        }

        foreach (var item in metadata.Screenshots)
        {
            yield return (item.RelativePath, item.Source);
        }
    }

    private static string NormalizeRelativePath(string value)
    {
        return value.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
