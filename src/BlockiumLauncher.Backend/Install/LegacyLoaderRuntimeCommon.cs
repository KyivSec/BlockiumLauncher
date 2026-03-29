using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace BlockiumLauncher.Infrastructure.Storage;

internal static class LegacyLoaderRuntimeCommon
{
    public static string[] MergeDistinct(string[] existing, string[] additional)
    {
        return existing
            .Concat(additional)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string BuildMavenArtifactPath(string coordinates)
    {
        var parts = coordinates.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            throw new InvalidOperationException("Invalid Maven coordinates: " + coordinates);
        }

        var groupId = parts[0];
        var artifactId = parts[1];
        var version = parts[2];
        var classifier = parts.Length >= 4 ? parts[3] : null;
        const string extension = "jar";

        var groupPath = groupId.Replace('.', '/');
        var fileName = string.IsNullOrWhiteSpace(classifier)
            ? $"{artifactId}-{version}.{extension}"
            : $"{artifactId}-{version}-{classifier}.{extension}";

        return $"{groupPath}/{artifactId}/{version}/{fileName}";
    }

    public static string CombineMavenUrl(string baseUrl, string relativePath)
    {
        var normalizedBase = baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : baseUrl + "/";
        return normalizedBase + relativePath;
    }

    public static async Task<RuntimeMetadataFile> ReadRuntimeMetadataAsync(string runtimeMetadataPath, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(runtimeMetadataPath, cancellationToken).ConfigureAwait(false);
        var value = JsonSerializer.Deserialize<RuntimeMetadataFile>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return value ?? new RuntimeMetadataFile();
    }

    public static async Task WriteRuntimeMetadataAsync(string runtimeMetadataPath, RuntimeMetadataFile runtimeMetadata, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(runtimeMetadata, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(runtimeMetadataPath, json, cancellationToken).ConfigureAwait(false);
    }

    public static bool IsLibraryAllowed(JsonElement library)
    {
        if (!library.TryGetProperty("rules", out var rulesElement) || rulesElement.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        var allowed = false;

        foreach (var rule in rulesElement.EnumerateArray())
        {
            var action = rule.TryGetProperty("action", out var actionElement) ? actionElement.GetString() : null;
            var applies = true;

            if (rule.TryGetProperty("os", out var osElement) && osElement.TryGetProperty("name", out var osNameElement))
            {
                applies = IsCurrentOs(osNameElement.GetString());
            }

            if (!applies)
            {
                continue;
            }

            if (string.Equals(action, "allow", StringComparison.OrdinalIgnoreCase))
            {
                allowed = true;
            }
            else if (string.Equals(action, "disallow", StringComparison.OrdinalIgnoreCase))
            {
                allowed = false;
            }
        }

        return allowed;
    }

    public static string? GetNativeClassifierKey(JsonElement library)
    {
        if (!library.TryGetProperty("natives", out var nativesElement))
        {
            return null;
        }

        if (OperatingSystem.IsWindows() && nativesElement.TryGetProperty("windows", out var windowsElement))
        {
            return windowsElement.GetString();
        }

        if (OperatingSystem.IsLinux() && nativesElement.TryGetProperty("linux", out var linuxElement))
        {
            return linuxElement.GetString();
        }

        if (OperatingSystem.IsMacOS() && nativesElement.TryGetProperty("osx", out var osxElement))
        {
            return osxElement.GetString();
        }

        return null;
    }

    public static void ExtractNativeArchive(string archivePath, string destinationDirectory)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            var normalizedPath = entry.FullName.Replace('\\', '/');
            if (normalizedPath.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destinationPath = Path.Combine(destinationDirectory, entry.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, true);
        }
    }

    public static async Task DownloadFileAsync(HttpClient httpClient, string url, string destinationPath, string? sha1, CancellationToken cancellationToken)
    {
        if (File.Exists(destinationPath) && !string.IsNullOrWhiteSpace(sha1))
        {
            var existingSha1 = await ComputeSha1Async(destinationPath, cancellationToken).ConfigureAwait(false);
            if (string.Equals(existingSha1, sha1, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var parentDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        var tempPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await using (var input = await httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(sha1))
            {
                var actualSha1 = await ComputeSha1Async(tempPath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(actualSha1, sha1, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Downloaded file hash mismatch.");
                }
            }

            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(tempPath, destinationPath);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }

            throw;
        }
    }

    public static string[] MergePreservingOrder(IEnumerable<string>? existing, IEnumerable<string>? incoming)
    {
        var result = new List<string>();

        if (existing is not null)
        {
            foreach (var item in existing)
            {
                if (!string.IsNullOrWhiteSpace(item))
                {
                    result.Add(item);
                }
            }
        }

        if (incoming is not null)
        {
            foreach (var item in incoming)
            {
                if (!string.IsNullOrWhiteSpace(item))
                {
                    result.Add(item);
                }
            }
        }

        return result.ToArray();
    }

    private static bool IsCurrentOs(string? osName)
    {
        return (OperatingSystem.IsWindows() && string.Equals(osName, "windows", StringComparison.OrdinalIgnoreCase))
            || (OperatingSystem.IsLinux() && string.Equals(osName, "linux", StringComparison.OrdinalIgnoreCase))
            || (OperatingSystem.IsMacOS() && string.Equals(osName, "osx", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string> ComputeSha1Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha1 = SHA1.Create();
        var hash = await sha1.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

internal sealed class LibraryDownloadWorkItem
{
    public string Url { get; init; } = string.Empty;
    public string DestinationPath { get; init; } = string.Empty;
    public string? Sha1 { get; init; }
    public bool AddToClasspath { get; init; }
}

internal sealed class RuntimeMetadataFile
{
    public string Version { get; set; } = string.Empty;
    public string MainClass { get; set; } = string.Empty;
    public string ClientJarPath { get; set; } = string.Empty;
    public string[] ClasspathEntries { get; set; } = [];
    public string AssetsDirectory { get; set; } = string.Empty;
    public string AssetIndexId { get; set; } = string.Empty;
    public string NativesDirectory { get; set; } = string.Empty;
    public string[] ExtraJvmArguments { get; set; } = [];
    public string[] ExtraGameArguments { get; set; } = [];
    public string LoaderType { get; set; } = string.Empty;
    public string LoaderVersion { get; set; } = string.Empty;
    public string LoaderProfileJsonPath { get; set; } = string.Empty;
}
