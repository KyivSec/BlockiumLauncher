using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BlockiumLauncher.Application.Abstractions.Paths;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Infrastructure.Persistence.Json;

namespace BlockiumLauncher.Application.UseCases.Catalog
{
    public sealed class PendingManualDownloadFile
    {
        public CatalogProvider Provider { get; init; } = CatalogProvider.CurseForge;
        public CatalogContentType ContentType { get; init; } = CatalogContentType.Mod;
        public string ProjectId { get; init; } = string.Empty;
        public string? ProjectName { get; init; }
        public string? IconUrl { get; init; }
        public string FileId { get; init; } = string.Empty;
        public string? ManifestFileId { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
        public string? ManifestFileName { get; init; }
        public string DestinationRelativePath { get; init; } = string.Empty;
        public string? DirectDownloadUrl { get; init; }
        public string? ProjectUrl { get; init; }
        public string? FilePageUrl { get; init; }
        public string? Sha1 { get; init; }
        public string? ManifestSha1 { get; init; }
        public long SizeBytes { get; init; }
        public long ManifestSizeBytes { get; init; }
    }

    public sealed class PendingManualDownloadsState
    {
        public CatalogProvider Provider { get; init; } = CatalogProvider.CurseForge;
        public string ModpackProjectId { get; init; } = string.Empty;
        public string ModpackFileId { get; init; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; init; }
        public IReadOnlyList<PendingManualDownloadFile> Files { get; init; } = [];
    }

    public sealed class ResumeCatalogModpackImportRequest
    {
        public string? InstanceId { get; init; }
        public string? InstanceName { get; init; }
        public string? DownloadsDirectory { get; init; }
        public bool WaitForManualDownloads { get; init; }
        public TimeSpan WaitTimeout { get; init; } = TimeSpan.FromMinutes(30);
    }

    public sealed class ResumeCatalogModpackImportResult
    {
        public Domain.Entities.LauncherInstance Instance { get; init; } = default!;
        public string DownloadsDirectory { get; init; } = string.Empty;
        public IReadOnlyList<PendingManualDownloadFile> PendingManualDownloads { get; init; } = [];
        public IReadOnlyList<string> ImportedFiles { get; init; } = [];
        public bool IsCompleted => PendingManualDownloads.Count == 0;
    }

    public enum BlockedModpackFilesDecision
    {
        Cancel = 0,
        Continue = 1,
        SkipMissing = 2
    }

    public sealed class BlockedModpackFilesPromptRequest
    {
        public CatalogProvider Provider { get; init; } = CatalogProvider.CurseForge;
        public string DownloadsDirectory { get; init; } = string.Empty;
        public IReadOnlyList<PendingManualDownloadFile> Files { get; init; } = [];
    }

    public sealed class BlockedModpackFilesPromptResult
    {
        public BlockedModpackFilesDecision Decision { get; init; } = BlockedModpackFilesDecision.Cancel;
        public IReadOnlyList<PendingManualDownloadMatch> Matches { get; init; } = [];
    }

    public sealed record PendingManualDownloadMatch(
        PendingManualDownloadFile File,
        string DownloadedPath);

    public static class PendingManualDownloadMatcher
    {
        public static IReadOnlyList<PendingManualDownloadMatch> FindMatches(
            string downloadsDirectory,
            IReadOnlyList<PendingManualDownloadFile> files)
        {
            ArgumentNullException.ThrowIfNull(files);

            if (files.Count == 0 || string.IsNullOrWhiteSpace(downloadsDirectory) || !Directory.Exists(downloadsDirectory))
            {
                return [];
            }

            var matches = new List<PendingManualDownloadMatch>();
            foreach (var file in files)
            {
                var matchedPath = TryFindDownloadedFile(downloadsDirectory, file);
                if (!string.IsNullOrWhiteSpace(matchedPath))
                {
                    matches.Add(new PendingManualDownloadMatch(file, matchedPath));
                }
            }

            return matches;
        }

        public static string? TryFindDownloadedFile(string downloadsDirectory, PendingManualDownloadFile file)
        {
            ArgumentNullException.ThrowIfNull(file);
            return TryFindDownloadedFile(downloadsDirectory, file.FileName, file.SizeBytes, file.Sha1);
        }

        public static string? TryFindDownloadedFile(
            string downloadsDirectory,
            string fileName,
            long sizeBytes,
            string? sha1 = null)
        {
            if (string.IsNullOrWhiteSpace(downloadsDirectory) ||
                string.IsNullOrWhiteSpace(fileName) ||
                !Directory.Exists(downloadsDirectory))
            {
                return null;
            }

            foreach (var candidatePath in Directory.EnumerateFiles(downloadsDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                if (!IsMatchingFileName(candidatePath, fileName) ||
                    !IsMatchingDownloadedFile(candidatePath, sizeBytes, sha1))
                {
                    continue;
                }

                return candidatePath;
            }

            return null;
        }

        private static bool IsMatchingFileName(string candidatePath, string expectedFileName)
        {
            var candidateName = Path.GetFileName(candidatePath);
            if (string.Equals(candidateName, expectedFileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var expectedStem = Path.GetFileNameWithoutExtension(expectedFileName);
            var expectedExtension = Path.GetExtension(expectedFileName);
            var candidateStem = Path.GetFileNameWithoutExtension(candidateName);
            var candidateExtension = Path.GetExtension(candidateName);

            if (!string.Equals(expectedExtension, candidateExtension, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(candidateStem, expectedStem, StringComparison.OrdinalIgnoreCase) ||
                   Regex.IsMatch(
                       candidateStem,
                       $"^{Regex.Escape(expectedStem)} \\(\\d+\\)$",
                       RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool IsMatchingDownloadedFile(string candidatePath, long sizeBytes, string? sha1)
        {
            if (!File.Exists(candidatePath))
            {
                return false;
            }

            var extension = Path.GetExtension(candidatePath);
            if (string.Equals(extension, ".crdownload", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".part", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".tmp", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(candidatePath);
                if (sizeBytes > 0 && fileInfo.Length != sizeBytes)
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(sha1))
                {
                    return sizeBytes <= 0 || fileInfo.Length == sizeBytes;
                }

                return string.Equals(
                    ComputeSha1(candidatePath),
                    NormalizeSha1(sha1),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string ComputeSha1(string candidatePath)
        {
            using var stream = File.OpenRead(candidatePath);
            using var hasher = SHA1.Create();
            return Convert.ToHexString(hasher.ComputeHash(stream));
        }

        private static string NormalizeSha1(string value)
        {
            return value.Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        }
    }
}

namespace BlockiumLauncher.Infrastructure.Services
{
    using BlockiumLauncher.Application.UseCases.Catalog;

    public sealed class JsonManualDownloadStateStore : IManualDownloadStateStore
    {
        private readonly ILauncherPaths launcherPaths;
        private readonly JsonFileStore jsonFileStore;

        public JsonManualDownloadStateStore(
            ILauncherPaths launcherPaths,
            JsonFileStore jsonFileStore)
        {
            this.launcherPaths = launcherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
            this.jsonFileStore = jsonFileStore ?? throw new ArgumentNullException(nameof(jsonFileStore));
        }

        public Task<PendingManualDownloadsState?> LoadAsync(string installLocation, CancellationToken cancellationToken = default)
        {
            return jsonFileStore.ReadOptionalAsync<PendingManualDownloadsState>(GetFilePath(installLocation), cancellationToken);
        }

        public Task SaveAsync(string installLocation, PendingManualDownloadsState state, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(state);
            return jsonFileStore.WriteAsync(GetFilePath(installLocation), state, cancellationToken);
        }

        public Task DeleteAsync(string installLocation, CancellationToken cancellationToken = default)
        {
            var filePath = GetFilePath(installLocation);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            return Task.CompletedTask;
        }

        private string GetFilePath(string installLocation)
        {
            return Path.Combine(launcherPaths.GetInstanceDataDirectory(installLocation), "pending-manual-downloads.json");
        }
    }
}
