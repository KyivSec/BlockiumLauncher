using System.Text.Json;
using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.Abstractions.Paths;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.UseCases.Catalog;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Backend.Catalog;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Downloads;
using BlockiumLauncher.Infrastructure.Metadata.Clients;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using BlockiumLauncher.Shared.Primitives;
using BlockiumLauncher.Shared.Results;
using Xunit;

namespace BlockiumLauncher.Application.Tests.Catalog;

public sealed class ModrinthModpackImportTests
{
    [Fact]
    public async Task ExecuteAsync_ImportsModrinthModpackAndCopiesOverrides()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var launcherPaths = new LauncherPaths(rootDirectory);
            var repository = new InMemoryInstanceRepository();
            var metadataService = new FakeInstanceContentMetadataService();
            var tempWorkspaceFactory = new FakeTempWorkspaceFactory();

            var installUseCase = new InstallInstanceUseCase(
                new InstallPlanBuilder(new FakeVersionManifestService(), new FakeLoaderMetadataService(), launcherPaths),
                tempWorkspaceFactory,
                new FakeInstanceContentInstaller(),
                new FakeFileTransaction(),
                repository,
                metadataService);
            var contentCatalog = new FakeContentCatalogFileService();
            var manualDownloadStateStore = new InMemoryManualDownloadStateStore();
            var pipeline = CreatePipeline(contentCatalog, installUseCase, metadataService, manualDownloadStateStore);

            var importUseCase = new ImportCatalogModpackUseCase(
                pipeline,
                contentCatalog,
                new FakeDownloader(),
                tempWorkspaceFactory,
                new FakeArchiveExtractor(),
                installUseCase,
                repository,
                metadataService,
                manualDownloadStateStore);

            var result = await importUseCase.ExecuteAsync(new ImportCatalogModpackRequest
            {
                Provider = CatalogProvider.Modrinth,
                ProjectId = "awesome-pack",
                FileId = "version-1",
                InstanceName = "Awesome Pack"
            });

            Assert.True(result.IsSuccess);
            Assert.True(result.Value.IsCompleted);
            Assert.True(File.Exists(Path.Combine(result.Value.InstalledPath, "mods", "example-mod.jar")));
            Assert.True(File.Exists(Path.Combine(result.Value.InstalledPath, "config", "pack.txt")));
            Assert.Contains("mods/example-mod.jar", metadataService.AppliedSources.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(ContentOriginProvider.Modrinth, metadataService.AppliedSources["mods/example-mod.jar"].Provider);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_UsesSelectedVersionAndLoaderFilters_WhenResolvingModpackFile()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var launcherPaths = new LauncherPaths(rootDirectory);
            var repository = new InMemoryInstanceRepository();
            var metadataService = new FakeInstanceContentMetadataService();
            var tempWorkspaceFactory = new FakeTempWorkspaceFactory();
            var contentCatalog = new FilteringFakeContentCatalogFileService();

            var installUseCase = new InstallInstanceUseCase(
                new InstallPlanBuilder(new FakeVersionManifestService(), new FakeLoaderMetadataService(), launcherPaths),
                tempWorkspaceFactory,
                new FakeInstanceContentInstaller(),
                new FakeFileTransaction(),
                repository,
                metadataService);
            var manualDownloadStateStore = new InMemoryManualDownloadStateStore();
            var pipeline = CreatePipeline(contentCatalog, installUseCase, metadataService, manualDownloadStateStore);

            var importUseCase = new ImportCatalogModpackUseCase(
                pipeline,
                contentCatalog,
                new FakeDownloader(),
                tempWorkspaceFactory,
                new FakeArchiveExtractor(),
                installUseCase,
                repository,
                metadataService,
                manualDownloadStateStore);

            var result = await importUseCase.ExecuteAsync(new ImportCatalogModpackRequest
            {
                Provider = CatalogProvider.Modrinth,
                ProjectId = "awesome-pack",
                InstanceName = "Awesome Pack",
                GameVersions = ["1.20.1", "1.21.1"],
                Loaders = ["fabric"]
            });

            Assert.True(result.IsSuccess);
            Assert.Equal("version-2", result.Value.File.FileId);
            Assert.Contains(contentCatalog.ObservedQueries, query => string.Equals(query.GameVersion, "1.20.1", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(contentCatalog.ObservedQueries, query => string.Equals(query.GameVersion, "1.21.1", StringComparison.OrdinalIgnoreCase));
            Assert.All(contentCatalog.ObservedQueries, query => Assert.Equal("fabric", query.Loader));
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
            => throw new NotSupportedException();

        public Task<Result<CatalogFileDetails>> GetFileDetailsAsync(CatalogFileDetailsQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<CatalogFileDetails>.Failure(new Shared.Errors.Error("Metadata.NotFound", "Not used in this test.")));

        public Task<Result<CatalogFileSummary>> ResolveFileAsync(CatalogFileResolutionQuery query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<CatalogFileSummary>.Success(new CatalogFileSummary
            {
                Provider = CatalogProvider.Modrinth,
                ContentType = CatalogContentType.Modpack,
                ProjectId = query.ProjectId,
                FileId = query.FileId ?? "version-1",
                DisplayName = "Awesome Pack",
                FileName = "awesome-pack.mrpack",
                DownloadUrl = "https://downloads.invalid/awesome-pack.mrpack"
            }));
        }
    }

    private sealed class FilteringFakeContentCatalogFileService : IContentCatalogFileService
    {
        public List<CatalogFileResolutionQuery> ObservedQueries { get; } = [];

        public Task<Result<IReadOnlyList<CatalogFileSummary>>> GetFilesAsync(CatalogFileQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<CatalogFileDetails>> GetFileDetailsAsync(CatalogFileDetailsQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<CatalogFileDetails>.Failure(new Shared.Errors.Error("Metadata.NotFound", "Not used in this test.")));

        public Task<Result<CatalogFileSummary>> ResolveFileAsync(CatalogFileResolutionQuery query, CancellationToken cancellationToken = default)
        {
            ObservedQueries.Add(query);

            if (query.ContentType != CatalogContentType.Modpack)
            {
                throw new NotSupportedException();
            }

            if (string.Equals(query.GameVersion, "1.21.1", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(query.Loader, "fabric", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(Result<CatalogFileSummary>.Success(new CatalogFileSummary
                {
                    Provider = CatalogProvider.Modrinth,
                    ContentType = CatalogContentType.Modpack,
                    ProjectId = query.ProjectId,
                    FileId = "version-2",
                    DisplayName = "Awesome Pack 2",
                    FileName = "awesome-pack-2.mrpack",
                    DownloadUrl = "https://downloads.invalid/awesome-pack-2.mrpack",
                    PublishedAtUtc = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
                    GameVersions = ["1.21.1"],
                    Loaders = ["fabric"]
                }));
            }

            if (string.Equals(query.GameVersion, "1.20.1", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(query.Loader, "fabric", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(Result<CatalogFileSummary>.Success(new CatalogFileSummary
                {
                    Provider = CatalogProvider.Modrinth,
                    ContentType = CatalogContentType.Modpack,
                    ProjectId = query.ProjectId,
                    FileId = "version-1",
                    DisplayName = "Awesome Pack 1",
                    FileName = "awesome-pack-1.mrpack",
                    DownloadUrl = "https://downloads.invalid/awesome-pack-1.mrpack",
                    PublishedAtUtc = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
                    GameVersions = ["1.20.1"],
                    Loaders = ["fabric"]
                }));
            }

            return Task.FromResult(Result<CatalogFileSummary>.Failure(new Shared.Errors.Error(
                "Metadata.NotFound",
                "No compatible file.")));
        }
    }

    private sealed class FakeArchiveExtractor : IArchiveExtractor
    {
        public Task<Result<Unit>> ExtractAsync(string ArchivePath, string DestinationPath, CancellationToken CancellationToken = default)
        {
            Directory.CreateDirectory(Path.Combine(DestinationPath, "overrides", "config"));
            File.WriteAllText(
                Path.Combine(DestinationPath, "modrinth.index.json"),
                JsonSerializer.Serialize(new
                {
                    dependencies = new Dictionary<string, string>
                    {
                        ["minecraft"] = "1.20.1",
                        ["fabric-loader"] = "0.16.9"
                    },
                    files = new[]
                    {
                        new
                        {
                            path = "mods/example-mod.jar",
                            downloads = new[] { "https://downloads.invalid/example-mod.jar" },
                            hashes = new { sha1 = "" },
                            project_id = "proj-1",
                            file_id = "file-1"
                        }
                    }
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

        public async Task<Result<DownloadBatchResult>> DownloadBatchAsync(
            DownloadBatchRequest Request,
            IProgress<DownloadBatchProgress>? Progress = null,
            CancellationToken CancellationToken = default)
        {
            var results = new List<DownloadResult>();
            long totalBytes = 0;

            for (var index = 0; index < Request.Requests.Count; index++)
            {
                var result = await DownloadAsync(Request.Requests[index], CancellationToken);
                if (result.IsFailure)
                {
                    return Result<DownloadBatchResult>.Failure(result.Error);
                }

                results.Add(result.Value);
                totalBytes += result.Value.BytesWritten;
                Progress?.Report(new DownloadBatchProgress(
                    index + 1,
                    Request.Requests.Count,
                    totalBytes,
                    totalBytes,
                    Path.GetFileName(Request.Requests[index].DestinationPath)));
            }

            return Result<DownloadBatchResult>.Success(new DownloadBatchResult(results, totalBytes));
        }
    }

    private sealed class FakeTempWorkspaceFactory : ITempWorkspaceFactory
    {
        public Task<ITempWorkspace> CreateAsync(string OperationName, CancellationToken CancellationToken = default)
            => Task.FromResult<ITempWorkspace>(new FakeTempWorkspace());
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
        public Task<Result<string>> PrepareAsync(
            InstallPlan Plan,
            ITempWorkspace Workspace,
            IProgress<InstallPreparationProgress>? Progress = null,
            CancellationToken CancellationToken = default)
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
            => Task.FromResult(Result<Unit>.Success(Unit.Value));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

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
            => Task.FromResult<IReadOnlyList<LauncherInstance>>(instances.ToList());

        public Task<LauncherInstance?> GetByIdAsync(InstanceId InstanceId, CancellationToken CancellationToken)
            => Task.FromResult<LauncherInstance?>(instances.FirstOrDefault(instance => instance.InstanceId.Equals(InstanceId)));

        public Task<LauncherInstance?> GetByNameAsync(string Name, CancellationToken CancellationToken)
            => Task.FromResult<LauncherInstance?>(instances.FirstOrDefault(instance => string.Equals(instance.Name, Name, StringComparison.OrdinalIgnoreCase)));

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
        public Task<PendingManualDownloadsState?> LoadAsync(string installLocation, CancellationToken cancellationToken = default)
            => Task.FromResult<PendingManualDownloadsState?>(null);

        public Task SaveAsync(string installLocation, PendingManualDownloadsState state, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(string installLocation, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeInstanceContentMetadataService : IInstanceContentMetadataService
    {
        public Dictionary<string, ContentSourceMetadata> AppliedSources { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<InstanceContentMetadata?> GetAsync(LauncherInstance instance, bool reindexIfMissing = false, CancellationToken cancellationToken = default)
            => Task.FromResult<InstanceContentMetadata?>(new InstanceContentMetadata());

        public Task<InstanceContentMetadata> ReindexAsync(LauncherInstance instance, CancellationToken cancellationToken = default)
            => Task.FromResult(new InstanceContentMetadata());

        public Task<InstanceContentMetadata> SetModEnabledAsync(LauncherInstance instance, string modReference, bool enabled, CancellationToken cancellationToken = default)
            => Task.FromResult(new InstanceContentMetadata());

        public Task<InstanceContentMetadata> SetContentEnabledAsync(LauncherInstance instance, InstanceContentCategory category, string contentReference, bool enabled, CancellationToken cancellationToken = default)
            => Task.FromResult(new InstanceContentMetadata());

        public Task<InstanceContentMetadata> DeleteContentAsync(LauncherInstance instance, InstanceContentCategory category, string contentReference, CancellationToken cancellationToken = default)
            => Task.FromResult(new InstanceContentMetadata());

        public Task<InstanceContentMetadata> RecordLaunchAsync(LauncherInstance instance, DateTimeOffset startedAtUtc, DateTimeOffset exitedAtUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(new InstanceContentMetadata());

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
            => Task.FromResult(Result<IReadOnlyList<VersionSummary>>.Success([
                new VersionSummary(VersionId.Parse("1.20.1"), "1.20.1", true, DateTimeOffset.UtcNow)
            ]));

        public Task<Result<VersionSummary?>> GetVersionAsync(VersionId VersionId, CancellationToken CancellationToken)
            => Task.FromResult(Result<VersionSummary?>.Success(new VersionSummary(VersionId, VersionId.ToString(), true, DateTimeOffset.UtcNow)));
    }

    private sealed class FakeLoaderMetadataService : ILoaderMetadataService
    {
        public Task<Result<IReadOnlyList<LoaderVersionSummary>>> GetLoaderVersionsAsync(LoaderType LoaderType, VersionId GameVersion, CancellationToken CancellationToken)
            => Task.FromResult(Result<IReadOnlyList<LoaderVersionSummary>>.Success([
                new LoaderVersionSummary(LoaderType.Fabric, VersionId.Parse("1.20.1"), "0.16.9", true)
            ]));
    }

    private static CatalogModpackImportPipeline CreatePipeline(
        IContentCatalogFileService contentCatalogFileService,
        InstallInstanceUseCase installUseCase,
        IInstanceContentMetadataService metadataService,
        IManualDownloadStateStore manualDownloadStateStore)
    {
        var httpClient = new HttpClient(new TestHttpMessageHandler(_ => throw new InvalidOperationException("CurseForge preflight should not run in Modrinth tests.")));
        var curseForgeService = new CurseForgeContentCatalogService(httpClient, new CurseForgeOptions
        {
            ApiKey = "test-api-key"
        });

        return new CatalogModpackImportPipeline(
            new CurseForgeModpackPreflightService(curseForgeService, contentCatalogFileService),
            contentCatalogFileService,
            new FakeDownloader(),
            installUseCase,
            metadataService,
            manualDownloadStateStore);
    }

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> handler;

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            this.handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
