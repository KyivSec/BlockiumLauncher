using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.Abstractions.Services;

namespace BlockiumLauncher.Infrastructure.Services;

public sealed class FileSystemInstanceContentIndexer : IInstanceContentIndexer
{
    public Task<InstanceContentMetadata> ScanAsync(
        string installLocation,
        InstanceContentMetadata? existingMetadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installLocation);
        cancellationToken.ThrowIfCancellationRequested();

        var root = Path.GetFullPath(installLocation);
        var existingSources = BuildExistingSourceMap(existingMetadata);

        var metadata = new InstanceContentMetadata
        {
            IndexedAtUtc = DateTimeOffset.UtcNow,
            IconPath = ResolveIconPath(root),
            TotalPlaytimeSeconds = existingMetadata?.TotalPlaytimeSeconds ?? 0,
            LastLaunchAtUtc = existingMetadata?.LastLaunchAtUtc,
            LastLaunchPlaytimeSeconds = existingMetadata?.LastLaunchPlaytimeSeconds,
            Mods = ScanFiles(root, existingSources, [".minecraft\\mods", "mods"]),
            ResourcePacks = ScanFiles(root, existingSources, [".minecraft\\resourcepacks", "resourcepacks"]),
            Shaders = ScanFiles(root, existingSources, [".minecraft\\shaderpacks", "shaderpacks"]),
            Worlds = ScanDirectories(root, existingSources, [".minecraft\\saves", "saves"]),
            Screenshots = ScanFiles(root, existingSources, [".minecraft\\screenshots", "screenshots"]),
            Servers = ScanServerFiles(root)
        };

        return Task.FromResult(metadata);
    }

    private static IReadOnlyDictionary<string, ContentSourceMetadata> BuildExistingSourceMap(InstanceContentMetadata? existingMetadata)
    {
        var map = new Dictionary<string, ContentSourceMetadata>(StringComparer.OrdinalIgnoreCase);
        if (existingMetadata is null)
        {
            return map;
        }

        foreach (var item in existingMetadata.Mods
                     .Concat(existingMetadata.ResourcePacks)
                     .Concat(existingMetadata.Shaders)
                     .Concat(existingMetadata.Worlds)
                     .Concat(existingMetadata.Screenshots))
        {
            if (!string.IsNullOrWhiteSpace(item.RelativePath) && item.Source is not null)
            {
                map[item.RelativePath] = item.Source;
            }
        }

        return map;
    }

    private static IReadOnlyList<InstanceFileMetadata> ScanFiles(
        string root,
        IReadOnlyDictionary<string, ContentSourceMetadata> existingSources,
        IReadOnlyList<string> relativeDirectories)
    {
        var results = new List<InstanceFileMetadata>();

        foreach (var directory in ResolveExistingPaths(root, relativeDirectories))
        {
            foreach (var filePath in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                var fileInfo = new FileInfo(filePath);
                var relativePath = NormalizeRelativePath(Path.GetRelativePath(root, filePath));

                results.Add(new InstanceFileMetadata
                {
                    Name = fileInfo.Name,
                    RelativePath = relativePath,
                    AbsolutePath = fileInfo.FullName,
                    SizeBytes = fileInfo.Length,
                    LastModifiedAtUtc = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : null,
                    IsDisabled = fileInfo.Name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase),
                    Source = existingSources.TryGetValue(relativePath, out var source) ? source : null
                });
            }
        }

        return results
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<InstanceFileMetadata> ScanDirectories(
        string root,
        IReadOnlyDictionary<string, ContentSourceMetadata> existingSources,
        IReadOnlyList<string> relativeDirectories)
    {
        var results = new List<InstanceFileMetadata>();

        foreach (var directory in ResolveExistingPaths(root, relativeDirectories))
        {
            foreach (var childDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                var directoryInfo = new DirectoryInfo(childDirectory);
                var relativePath = NormalizeRelativePath(Path.GetRelativePath(root, childDirectory));

                results.Add(new InstanceFileMetadata
                {
                    Name = directoryInfo.Name,
                    RelativePath = relativePath,
                    AbsolutePath = directoryInfo.FullName,
                    SizeBytes = 0,
                    LastModifiedAtUtc = directoryInfo.Exists ? directoryInfo.LastWriteTimeUtc : null,
                    Source = existingSources.TryGetValue(relativePath, out var source) ? source : null
                });
            }
        }

        return results
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<InstanceServerMetadata> ScanServerFiles(string root)
    {
        var results = new List<InstanceServerMetadata>();

        foreach (var relativePath in new[] { ".minecraft\\servers.dat", ".minecraft\\servers.dat_old", "servers.dat", "servers.dat_old" })
        {
            var fullPath = Path.Combine(root, relativePath);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var fileInfo = new FileInfo(fullPath);
            results.Add(new InstanceServerMetadata
            {
                RelativePath = NormalizeRelativePath(Path.GetRelativePath(root, fullPath)),
                AbsolutePath = fileInfo.FullName,
                SizeBytes = fileInfo.Length,
                LastModifiedAtUtc = fileInfo.LastWriteTimeUtc
            });
        }

        return results;
    }

    private static IEnumerable<string> ResolveExistingPaths(string root, IReadOnlyList<string> relativeDirectories)
    {
        foreach (var relativeDirectory in relativeDirectories)
        {
            var fullPath = Path.Combine(root, relativeDirectory);
            if (Directory.Exists(fullPath))
            {
                yield return fullPath;
            }
        }
    }

    private static string? ResolveIconPath(string root)
    {
        foreach (var relativePath in new[] { ".blockium\\icon.png", "instance.png", ".minecraft\\icon.png" })
        {
            var fullPath = Path.Combine(root, relativePath);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    private static string NormalizeRelativePath(string value)
    {
        return value.Replace(Path.DirectorySeparatorChar, '/');
    }
}
