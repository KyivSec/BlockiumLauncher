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
        public string FileId { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
        public string DestinationRelativePath { get; init; } = string.Empty;
        public string? ProjectUrl { get; init; }
        public string? FilePageUrl { get; init; }
        public string? Sha1 { get; init; }
        public long SizeBytes { get; init; }
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
