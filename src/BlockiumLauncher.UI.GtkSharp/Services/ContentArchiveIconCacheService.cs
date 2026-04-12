using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlockiumLauncher.Application.Abstractions.Paths;
using Gdk;

namespace BlockiumLauncher.UI.GtkSharp.Services;

public sealed class ContentArchiveIconCacheService
{
    private static readonly Regex ModsTomlLogoRegex = new(@"logoFile\s*=\s*[""'](?<path>[^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] CommonIconNames =
    [
        "icon.png",
        "pack.png",
        "logo.png",
        "assets/icon.png",
        "assets/pack.png"
    ];

    private readonly string cacheDirectory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> extractionGates = new(StringComparer.OrdinalIgnoreCase);

    public ContentArchiveIconCacheService(ILauncherPaths launcherPaths)
    {
        ArgumentNullException.ThrowIfNull(launcherPaths);

        cacheDirectory = Path.Combine(launcherPaths.CacheDirectory, "content-icons");
        Directory.CreateDirectory(cacheDirectory);
    }

    public async Task<Pixbuf?> LoadIconPixbufAsync(string archivePath, int squareSize, CancellationToken cancellationToken = default)
    {
        if (squareSize <= 0 || string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            return null;
        }

        var extension = Path.GetExtension(archivePath);
        if (!string.Equals(extension, ".jar", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var cachePath = GetCachePath(archivePath);
        if (File.Exists(cachePath))
        {
            return TryLoadScaledPixbuf(cachePath, squareSize);
        }

        var gate = extractionGates.GetOrAdd(cachePath, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(cachePath))
            {
                await ExtractArchiveIconAsync(archivePath, cachePath, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }

        return File.Exists(cachePath) ? TryLoadScaledPixbuf(cachePath, squareSize) : null;
    }

    private async Task ExtractArchiveIconAsync(string archivePath, string cachePath, CancellationToken cancellationToken)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var entry = await ResolveIconEntryAsync(archive, cancellationToken).ConfigureAwait(false);
            if (entry is null)
            {
                return;
            }

            await using var entryStream = entry.Open();
            using var memoryStream = new MemoryStream();
            await entryStream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            var pixbuf = TryLoadPixbuf(memoryStream.ToArray());
            if (pixbuf is null)
            {
                return;
            }

            try
            {
                pixbuf.Savev(cachePath, "png", Array.Empty<string>(), Array.Empty<string>());
            }
            finally
            {
                pixbuf.Dispose();
            }
        }
        catch
        {
        }
    }

    private static async Task<ZipArchiveEntry?> ResolveIconEntryAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        var entryMap = archive.Entries
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.FullName) && !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            .ToDictionary(static entry => NormalizeEntryPath(entry.FullName), static entry => entry, StringComparer.OrdinalIgnoreCase);

        var modsTomlEntry = FindEntry(entryMap, "META-INF/mods.toml");
        if (modsTomlEntry is not null)
        {
            var logoPath = await TryReadModsTomlLogoAsync(modsTomlEntry, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(logoPath) && FindEntry(entryMap, logoPath) is { } logoEntry)
            {
                return logoEntry;
            }
        }

        var fabricManifestEntry = FindEntry(entryMap, "fabric.mod.json");
        if (fabricManifestEntry is not null)
        {
            var iconPath = await TryReadFabricIconAsync(fabricManifestEntry, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(iconPath) && FindEntry(entryMap, iconPath) is { } fabricIconEntry)
            {
                return fabricIconEntry;
            }
        }

        var legacyMetadataEntry = FindEntry(entryMap, "mcmod.info");
        if (legacyMetadataEntry is not null)
        {
            var iconPath = await TryReadLegacyIconAsync(legacyMetadataEntry, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(iconPath) && FindEntry(entryMap, iconPath) is { } legacyIconEntry)
            {
                return legacyIconEntry;
            }
        }

        foreach (var iconName in CommonIconNames)
        {
            if (FindEntry(entryMap, iconName) is { } commonEntry)
            {
                return commonEntry;
            }
        }

        return archive.Entries.FirstOrDefault(static entry =>
            !string.IsNullOrWhiteSpace(entry.Name) &&
            (string.Equals(entry.Name, "icon.png", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(entry.Name, "pack.png", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(entry.Name, "logo.png", StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task<string?> TryReadModsTomlLogoAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        var text = await ReadEntryTextAsync(entry, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = ModsTomlLogoRegex.Match(text);
        return match.Success ? match.Groups["path"].Value.Trim() : null;
    }

    private static async Task<string?> TryReadFabricIconAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        var text = await ReadEntryTextAsync(entry, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (!document.RootElement.TryGetProperty("icon", out var iconElement))
            {
                return null;
            }

            if (iconElement.ValueKind == JsonValueKind.String)
            {
                return iconElement.GetString();
            }

            if (iconElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? selected = null;
            var bestSize = int.MinValue;
            foreach (var property in iconElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (int.TryParse(property.Name, out var parsedSize) && parsedSize > bestSize)
                {
                    bestSize = parsedSize;
                    selected = property.Value.GetString();
                }
                else if (selected is null)
                {
                    selected = property.Value.GetString();
                }
            }

            return selected;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> TryReadLegacyIconAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        var text = await ReadEntryTextAsync(entry, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    if (TryReadLegacyIconProperty(element, out var icon))
                    {
                        return icon;
                    }
                }

                return null;
            }

            return TryReadLegacyIconProperty(document.RootElement, out var value) ? value : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadLegacyIconProperty(JsonElement element, out string? iconPath)
    {
        iconPath = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (element.TryGetProperty("logoFile", out var logoElement) && logoElement.ValueKind == JsonValueKind.String)
        {
            iconPath = logoElement.GetString();
            return !string.IsNullOrWhiteSpace(iconPath);
        }

        if (element.TryGetProperty("logo", out var legacyLogoElement) && legacyLogoElement.ValueKind == JsonValueKind.String)
        {
            iconPath = legacyLogoElement.GetString();
            return !string.IsNullOrWhiteSpace(iconPath);
        }

        return false;
    }

    private static async Task<string?> ReadEntryTextAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private string GetCachePath(string archivePath)
    {
        var fileInfo = new FileInfo(archivePath);
        var cacheKey = $"{archivePath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
        var fileName = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(cacheKey)));
        return Path.Combine(cacheDirectory, $"{fileName}.png");
    }

    private static ZipArchiveEntry? FindEntry(IReadOnlyDictionary<string, ZipArchiveEntry> entryMap, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return null;
        }

        var normalized = NormalizeEntryPath(candidatePath);
        if (entryMap.TryGetValue(normalized, out var exactEntry))
        {
            return exactEntry;
        }

        var fileName = Path.GetFileName(normalized);
        foreach (var entry in entryMap)
        {
            if (entry.Key.EndsWith("/" + normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(entry.Key), fileName, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }

        return null;
    }

    private static string NormalizeEntryPath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static Pixbuf? TryLoadScaledPixbuf(string path, int squareSize)
    {
        try
        {
            using var original = new Pixbuf(path);
            if (original.Width == squareSize && original.Height == squareSize)
            {
                return original.Copy();
            }

            return original.ScaleSimple(squareSize, squareSize, InterpType.Bilinear);
        }
        catch
        {
            return null;
        }
    }

    private static Pixbuf? TryLoadPixbuf(byte[] bytes)
    {
        try
        {
            using var loader = new PixbufLoader();
            loader.Write(bytes);
            loader.Close();
            return loader.Pixbuf?.Copy();
        }
        catch
        {
            return null;
        }
    }
}
