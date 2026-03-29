using System.Text.Json;
using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.Abstractions.Paths;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.UseCases.Catalog;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Downloads;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using BlockiumLauncher.Shared.Primitives;
using BlockiumLauncher.Shared.Results;
using Xunit;

namespace BlockiumLauncher.Application.Tests.Catalog;

public sealed class CatalogManualDownloadFlowTests
{
    [Fact]
    public async Task ImportAndResumeModpackImport_CompletesWhenManualFilesAppearInDownloadsFolder()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests", Path.GetRandomFileName());
        var downloadsDirectory = Path.Combine(rootDirectory, "Downloads");
        Directory.CreateDirectory(downloadsDirectory);

        try
        {
            var launcherPaths = new LauncherPaths(rootDirectory);
            var repository = new InMemoryInstanceRepository();
            var metadataService = new FakeInstanceContentMetadataService();
            var manualDownloadStateStore = new InMemoryManualDownloadStateStore();
            var tempWorkspaceFactory = new FakeTempWorkspaceFactory();

            var installPlanBuilder = new InstallPlanBuilder(
                new FakeVersionManifestService(),
                new FakeLoaderMetadataService(),
                launcherPaths);

            var installUseCase = new InstallInstanceUseCase(
                installPlanBuilder,
                tempWorkspaceFactory,
                new FakeInstanceContentInstaller(),
                new FakeFileTransaction(),
                repository,
                metadataService);

            var importUseCase = new ImportCatalogModpackUseCase(
                new FakeContentCatalogFileService(),
                new FakeDownloader(),
                tempWorkspaceFactory,
                new FakeArchiveExtractor(),
                installUseCase,
                repository,
                metadataService,
                manualDownloadStateStore);

            var importResult = await importUseCase.ExecuteAsync(new ImportCatalogModpackRequest
            {
                Provider = CatalogProvider.CurseForge,
                ProjectId = "9000",
                FileId = "9001",
                InstanceName = "Manual Pack",
                DownloadsDirectory = downloadsDirectory
            });

            Assert.True(importResult.IsSuccess);
            Assert.False(importResult.Value.IsCompleted);
            Assert.Equal(downloadsDirectory, importResult.Value.DownloadsDirectory);

            var pendingFile = Assert.Single(importResult.Value.PendingManualDownloads);
            Assert.Equal("manual-only.jar", pendingFile.FileName);
            Assert.True(File.Exists(Path.Combine(importResult.Value.InstalledPath, "mods", "auto-downloaded.jar")));
            Assert.True(File.Exists(Path.Combine(importResult.Value.InstalledPath, "config", "pack.txt")));

            await File.WriteAllBytesAsync(
                Path.Combine(downloadsDirectory, pendingFile.FileName),
                new byte[pendingFile.SizeBytes == 0 ? 4 : pendingFile.SizeBytes]);

            var resumeUseCase = new ResumeCatalogModpackImportUseCase(
                repository,
                importUseCase,
                manualDownloadStateStore);

            var resumeResult = await resumeUseCase.ExecuteAsync(new ResumeCatalogModpackImportRequest
            {
                InstanceId = importResult.Value.Instance.InstanceId.ToString(),
                DownloadsDirectory = downloadsDirectory
            });

            Assert.True(resumeResult.IsSuccess);
            Assert.True(resumeResult.Value.IsCompleted);
            Assert.Empty(resumeResult.Value.PendingManualDownloads);
            Assert.True(File.Exists(Path.Combine(importResult.Value.InstalledPath, "mods", "manual-only.jar")));
            Assert.Contains("mods/manual-only.jar", metadataService.AppliedSources.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Null(await manualDownloadStateStore.LoadAsync(importResult.Value.Instance.InstallLocation));
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    private sealed class FakeContentCatalogFileService : IContentCatalogFileService
    {
        public Task<Result<IReadOnlyList<CatalogFileSummary>>> GetFilesAsync(CatalogFileQuery query, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<CatalogFileSummary>> ResolveFileAsync(CatalogFileResolutionQuery query, CancellationToken cancellationToken = default)
        {
            if (query.ContentType == CatalogContentType.Modpack)
            {
                return Task.FromResult(Result<CatalogFileSummary>.Success(new CatalogFileSummary
                {
                    Provider = CatalogProvider.CurseForge,
                    ContentType = CatalogContentType.Modpack,
                    ProjectId = query.ProjectId,
                    FileId = query.FileId ?? "9001",
                    DisplayName = "Manual Pack",
                    FileName = "manual-pack.zip",
                    DownloadUrl = "https://downloads.invalid/manual-pack.zip"
                }));
            }

            if (query.ProjectId == "1001")
            {
                return Task.FromResult(Result<CatalogFileSummary>.Success(new CatalogFileSummary
                {
                    Provider = CatalogProvider.CurseForge,
                    ContentType = CatalogContentType.Mod,
                    ProjectId = "1001",
                    FileId = "2001",
                    DisplayName = "Auto Downloaded",
                    FileName = "auto-downloaded.jar",
                    DownloadUrl = "https://downloads.invalid/auto-downloaded.jar",
                    SizeBytes = 4
                }));
            }

            return Task.FromResult(Result<CatalogFileSummary>.Success(new CatalogFileSummary
            {
                Provider = CatalogProvider.CurseForge,
                ContentType = CatalogContentType.Mod,
                ProjectId = "1002",
                FileId = "2002",
                DisplayName = "Manual Only",
                FileName = "manual-only.jar",
                DownloadUrl = null,
                ProjectUrl = "https://www.curseforge.com/minecraft/mc-mods/manual-only",
                FilePageUrl = "https://www.curseforge.com/minecraft/mc-mods/manual-only/files/2002",
                SizeBytes = 4,
                RequiresManualDownload = true
            }));
        }
    }

    private sealed class FakeArchiveExtractor : IArchiveExtractor
    {
        public Task<Result<Unit>> ExtractAsync(string ArchivePath, string DestinationPath, CancellationToken CancellationToken = default)
        {
            Directory.CreateDirectory(Path.Combine(DestinationPath, "overrides", "config"));
            File.WriteAllText(
                Path.Combine(DestinationPath, "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    minecraft = new
                    {
                        version = "1.20.1",
                        modLoaders = new[]
                        {
                            new { id = "forge-47.3.12", primary = true }
                        }
                    },
                    files = new[]
                    {
                        new { projectID = 1001, fileID = 2001 },
                        new { projectID = 1002, fileID = 2002 }
                    },
                    overrides = "overrides"
                }));
            File.WriteAllText(Path.Combine(DestinationPath, "overrides", "config", "pack.txt"), "configured");
            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }
    }

    private sealed class FakeDownloader : IDownloader
    {
        public Task<Result<DownloadResult>> DownloadAsync(DownloadRequest Request, CancellationToken CancellationToken)
        {
            var directory = Path.GetDirectoryName(Request.DestinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(Request.DestinationPath, [1, 2, 3, 4]);
            return Task.FromResult(Result<DownloadResult>.Success(new DownloadResult(Request.DestinationPath, 4)));
        }
    }

    private sealed class FakeTempWorkspaceFactory : ITempWorkspaceFactory
    {
        public Task<ITempWorkspace> CreateAsync(string OperationName, CancellationToken CancellationToken = default)
        {
            return Task.FromResult<ITempWorkspace>(new FakeTempWorkspace());
        }
    }

    private sealed class FakeTempWorkspace : ITempWorkspace
    {
        public string RootPath { get; } = Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests", Path.GetRandomFileName());

        public FakeTempWorkspace()
        {
            Directory.CreateDirectory(RootPath);
        }

        public string GetPath(string RelativePath) => Path.Combine(RootPath, RelativePath);

        public Task CreateDirectoryAsync(string RelativePath, CancellationToken CancellationToken = default)
        {
            Directory.CreateDirectory(GetPath(RelativePath));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeInstanceContentInstaller : IInstanceContentInstaller
    {
        public Task<Result<string>> PrepareAsync(InstallPlan Plan, ITempWorkspace Workspace, CancellationToken CancellationToken = default)
        {
            var stagedRoot = Workspace.GetPath("prepared");
            Directory.CreateDirectory(Path.Combine(stagedRoot, ".minecraft", "mods"));
            Directory.CreateDirectory(Path.Combine(stagedRoot, ".minecraft", "config"));
            Directory.CreateDirectory(Path.Combine(stagedRoot, ".blockium"));
            return Task.FromResult(Result<string>.Success(stagedRoot));
        }
    }

    private sealed class FakeFileTransaction : IFileTransaction
    {
        private string? sourceDirectory;

        public string? TargetRootPath { get; private set; }

        public Task<Result<Unit>> BeginAsync(string TargetRootPath, CancellationToken CancellationToken = default)
        {
            this.TargetRootPath = TargetRootPath;
            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }

        public Task<Result<Unit>> StageDirectoryAsync(string SourceDirectoryPath, CancellationToken CancellationToken = default)
        {
            sourceDirectory = SourceDirectoryPath;
            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }

        public Task<Result<Unit>> CommitAsync(CancellationToken CancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(TargetRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
            CopyDirectory(sourceDirectory!, TargetRootPath!, overwrite: true);
            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }

        public Task<Result<Unit>> RollbackAsync(CancellationToken CancellationToken = default)
        {
            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory, bool overwrite)
        {
            Directory.CreateDirectory(destinationDirectory);

            foreach (var directoryPath in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, directoryPath);
                Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
            }

            foreach (var filePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
                var destinationPath = Path.Combine(destinationDirectory, relativePath);
                var destinationParent = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationParent))
                {
                    Directory.CreateDirectory(destinationParent);
                }

                File.Copy(filePath, destinationPath, overwrite);
            }
        }
    }

    private sealed class InMemoryInstanceRepository : IInstanceRepository
    {
        private readonly List<LauncherInstance> instances = [];

        public Task<IReadOnlyList<LauncherInstance>> ListAsync(CancellationToken CancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LauncherInstance>>(instances.ToList());
        }

        public Task<LauncherInstance?> GetByIdAsync(InstanceId InstanceId, CancellationToken CancellationToken)
        {
            return Task.FromResult<LauncherInstance?>(instances.FirstOrDefault(instance => instance.InstanceId.Equals(InstanceId)));
        }

        public Task<LauncherInstance?> GetByNameAsync(string Name, CancellationToken CancellationToken)
        {
            return Task.FromResult<LauncherInstance?>(instances.FirstOrDefault(instance => string.Equals(instance.Name, Name, StringComparison.OrdinalIgnoreCase)));
        }

        public Task SaveAsync(LauncherInstance Instance, CancellationToken CancellationToken)
        {
            var existingIndex = instances.FindIndex(item => item.InstanceId.Equals(Instance.InstanceId));
            if (existingIndex >= 0)
            {
                instances[existingIndex] = Instance;
            }
            else
            {
                instances.Add(Instance);
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(InstanceId InstanceId, CancellationToken CancellationToken)
        {
            instances.RemoveAll(instance => instance.InstanceId.Equals(InstanceId));
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryManualDownloadStateStore : IManualDownloadStateStore
    {
        private readonly Dictionary<string, PendingManualDownloadsState> states = new(StringComparer.OrdinalIgnoreCase);

        public Task<PendingManualDownloadsState?> LoadAsync(string installLocation, CancellationToken cancellationToken = default)
        {
            states.TryGetValue(installLocation, out var state);
            return Task.FromResult<PendingManualDownloadsState?>(state);
        }

        public Task SaveAsync(string installLocation, PendingManualDownloadsState state, CancellationToken cancellationToken = default)
        {
            states[installLocation] = state;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string installLocation, CancellationToken cancellationToken = default)
        {
            states.Remove(installLocation);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInstanceContentMetadataService : IInstanceContentMetadataService
    {
        public Dictionary<string, ContentSourceMetadata> AppliedSources { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<InstanceContentMetadata?> GetAsync(LauncherInstance instance, bool reindexIfMissing = false, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<InstanceContentMetadata?>(new InstanceContentMetadata());
        }

        public Task<InstanceContentMetadata> ReindexAsync(LauncherInstance instance, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new InstanceContentMetadata());
        }

        public Task<InstanceContentMetadata> SetModEnabledAsync(LauncherInstance instance, string modReference, bool enabled, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new InstanceContentMetadata());
        }

        public Task<InstanceContentMetadata> RecordLaunchAsync(LauncherInstance instance, DateTimeOffset startedAtUtc, DateTimeOffset exitedAtUtc, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new InstanceContentMetadata());
        }

        public Task<InstanceContentMetadata> ApplySourcesAsync(LauncherInstance instance, IReadOnlyDictionary<string, ContentSourceMetadata> sourcesByRelativePath, CancellationToken cancellationToken = default)
        {
            foreach (var pair in sourcesByRelativePath)
            {
                AppliedSources[pair.Key] = pair.Value;
            }

            return Task.FromResult(new InstanceContentMetadata());
        }
    }

    private sealed class FakeVersionManifestService : IVersionManifestService
    {
        public Task<Result<IReadOnlyList<VersionSummary>>> GetAvailableVersionsAsync(CancellationToken CancellationToken)
        {
            return Task.FromResult(Result<IReadOnlyList<VersionSummary>>.Success([
                new VersionSummary(VersionId.Parse("1.20.1"), "1.20.1", true, DateTimeOffset.UtcNow)
            ]));
        }

        public Task<Result<VersionSummary?>> GetVersionAsync(VersionId VersionId, CancellationToken CancellationToken)
        {
            return Task.FromResult(Result<VersionSummary?>.Success(new VersionSummary(VersionId, VersionId.ToString(), true, DateTimeOffset.UtcNow)));
        }
    }

    private sealed class FakeLoaderMetadataService : ILoaderMetadataService
    {
        public Task<Result<IReadOnlyList<LoaderVersionSummary>>> GetLoaderVersionsAsync(LoaderType LoaderType, VersionId GameVersion, CancellationToken CancellationToken)
        {
            return Task.FromResult(Result<IReadOnlyList<LoaderVersionSummary>>.Success([
                new LoaderVersionSummary(LoaderType.Forge, VersionId.Parse("1.20.1"), "47.3.12", true)
            ]));
        }
    }
}
