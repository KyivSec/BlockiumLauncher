using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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

public sealed class CatalogManualDownloadFlowTests
{
    [Fact]
    public async Task ExecuteAsync_CompletesImportAfterBlockedFilesAreMatchedBeforeDownloadsStart()
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
            var contentCatalog = new FakeContentCatalogFileService();

            var installUseCase = new InstallInstanceUseCase(
                new InstallPlanBuilder(new FakeVersionManifestService(), new FakeLoaderMetadataService(), launcherPaths),
                tempWorkspaceFactory,
                new FakeInstanceContentInstaller(),
                new FakeFileTransaction(),
                repository,
                metadataService);

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

            var promptWasShown = false;
            var importResult = await importUseCase.ExecuteAsync(new ImportCatalogModpackRequest
            {
                Provider = CatalogProvider.CurseForge,
                ProjectId = "9000",
                FileId = "9001",
                InstanceName = "Manual Pack",
                DownloadsDirectory = downloadsDirectory,
                BlockedFilesPromptAsync = async (prompt, cancellationToken) =>
                {
                    promptWasShown = true;
                    Assert.Equal(CatalogProvider.CurseForge, prompt.Provider);
                    Assert.Equal(downloadsDirectory, prompt.DownloadsDirectory);

                    var blockedFile = Assert.Single(prompt.Files);
                    Assert.Equal("Manual Only", blockedFile.ProjectName);
                    Assert.Equal("2003", blockedFile.FileId);
                    Assert.Equal("2002", blockedFile.ManifestFileId);
                    Assert.Equal("manual-only-1.20.1.jar", blockedFile.FileName);
                    Assert.Equal("manual-only-1.20.4.jar", blockedFile.ManifestFileName);
                    Assert.Equal("https://www.curseforge.com/minecraft/mc-mods/manual-only/files/2003", blockedFile.FilePageUrl);
                    Assert.Equal("https://www.curseforge.com/minecraft/mc-mods/manual-only/download/2003/file", blockedFile.DirectDownloadUrl);

                    await File.WriteAllBytesAsync(
                        Path.Combine(downloadsDirectory, blockedFile.FileName),
                        [1, 2, 3, 4],
                        cancellationToken);

                    return new BlockedModpackFilesPromptResult
                    {
                        Decision = BlockedModpackFilesDecision.Continue,
                        Matches = PendingManualDownloadMatcher.FindMatches(prompt.DownloadsDirectory, prompt.Files)
                    };
                }
            });

            Assert.True(importResult.IsSuccess);
            Assert.True(promptWasShown);
            Assert.True(importResult.Value.IsCompleted);
            Assert.False(importResult.Value.WasManualDownloadStepSkipped);
            Assert.Empty(importResult.Value.PendingManualDownloads);
            Assert.Equal(downloadsDirectory, importResult.Value.DownloadsDirectory);
            Assert.True(File.Exists(Path.Combine(importResult.Value.InstalledPath, "mods", "auto-downloaded.jar")));
            Assert.True(File.Exists(Path.Combine(importResult.Value.InstalledPath, "mods", "manual-only-1.20.1.jar")));
            Assert.False(File.Exists(Path.Combine(importResult.Value.InstalledPath, "mods", "manual-only-1.20.4.jar")));
            Assert.True(File.Exists(Path.Combine(importResult.Value.InstalledPath, "config", "pack.txt")));
            Assert.Contains("mods/manual-only-1.20.1.jar", metadataService.AppliedSources.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(
                "https://www.curseforge.com/minecraft/mc-mods/manual-only/download/2003/file",
                metadataService.AppliedSources["mods/manual-only-1.20.1.jar"].OriginalUrl);
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

    [Fact]
    public async Task ExecuteAsync_CancelInBlockedFilesPrompt_DoesNotSaveInstance()
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
            var contentCatalog = new FakeContentCatalogFileService();

            var installUseCase = new InstallInstanceUseCase(
                new InstallPlanBuilder(new FakeVersionManifestService(), new FakeLoaderMetadataService(), launcherPaths),
                tempWorkspaceFactory,
                new FakeInstanceContentInstaller(),
                new FakeFileTransaction(),
                repository,
                metadataService);

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

            await Assert.ThrowsAsync<OperationCanceledException>(() => importUseCase.ExecuteAsync(new ImportCatalogModpackRequest
            {
                Provider = CatalogProvider.CurseForge,
                ProjectId = "9000",
                FileId = "9001",
                InstanceName = "Cancelled Pack",
                DownloadsDirectory = downloadsDirectory,
                BlockedFilesPromptAsync = (_, _) => Task.FromResult(new BlockedModpackFilesPromptResult
                {
                    Decision = BlockedModpackFilesDecision.Cancel
                })
            }));

            Assert.Empty(await repository.ListAsync(CancellationToken.None));
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

        public Task<Result<CatalogFileDetails>> GetFileDetailsAsync(CatalogFileDetailsQuery query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<CatalogFileDetails>.Failure(new Shared.Errors.Error("Metadata.NotFound", "Not used in this test.")));
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

            if (query.ProjectId == "1002" &&
                string.Equals(query.GameVersion, "1.20.1", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(Result<CatalogFileSummary>.Success(new CatalogFileSummary
                {
                    Provider = CatalogProvider.CurseForge,
                    ContentType = CatalogContentType.Mod,
                    ProjectId = "1002",
                    FileId = "2003",
                    DisplayName = "Manual Only",
                    FileName = "manual-only-1.20.1.jar",
                    DownloadUrl = null,
                    ProjectUrl = "https://www.curseforge.com/minecraft/mc-mods/manual-only",
                    FilePageUrl = "https://www.curseforge.com/minecraft/mc-mods/manual-only/files/2003",
                    Sha1 = TestSha1,
                    SizeBytes = 4,
                    PublishedAtUtc = DateTimeOffset.UtcNow,
                    GameVersions = ["1.20.1", "Forge"],
                    Loaders = ["forge"],
                    RequiresManualDownload = true
                }));
            }

            throw new NotSupportedException($"Unexpected resolution query: {query.ProjectId} / {query.ContentType} / {query.GameVersion}");
        }
    }

    private static string TestSha1 => Convert.ToHexString(SHA1.HashData([1, 2, 3, 4]));

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

        public Task<InstanceContentMetadata> SetContentEnabledAsync(LauncherInstance instance, InstanceContentCategory category, string contentReference, bool enabled, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new InstanceContentMetadata());
        }

        public Task<InstanceContentMetadata> DeleteContentAsync(LauncherInstance instance, InstanceContentCategory category, string contentReference, CancellationToken cancellationToken = default)
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

    private static CatalogModpackImportPipeline CreatePipeline(
        IContentCatalogFileService contentCatalogFileService,
        InstallInstanceUseCase installUseCase,
        IInstanceContentMetadataService metadataService,
        IManualDownloadStateStore manualDownloadStateStore)
    {
        var httpClient = new HttpClient(new TestHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Post &&
                string.Equals(request.RequestUri?.AbsoluteUri, "https://api.curseforge.com/v1/mods/files", StringComparison.OrdinalIgnoreCase))
            {
                return CreateJsonResponse($$"""
                {
                  "data": [
                    {
                      "id": 2001,
                      "modId": 1001,
                      "displayName": "Auto Downloaded",
                      "fileName": "auto-downloaded.jar",
                      "downloadUrl": "https://downloads.invalid/auto-downloaded.jar",
                      "fileDate": "2026-04-01T00:00:00Z",
                      "fileLength": 4,
                      "hashes": [
                        { "algo": 1, "value": "{{TestSha1}}" }
                      ],
                      "gameVersions": ["1.20.1", "Forge"]
                    },
                    {
                      "id": 2002,
                      "modId": 1002,
                      "displayName": "Manual Only",
                      "fileName": "manual-only-1.20.4.jar",
                      "downloadUrl": null,
                      "fileDate": "2026-03-01T00:00:00Z",
                      "fileLength": 4,
                      "hashes": [
                        { "algo": 1, "value": "{{TestSha1}}" }
                      ],
                      "gameVersions": ["1.20.4", "Forge"]
                    }
                  ]
                }
                """);
            }

            if (request.Method == HttpMethod.Post &&
                string.Equals(request.RequestUri?.AbsoluteUri, "https://api.curseforge.com/v1/mods", StringComparison.OrdinalIgnoreCase))
            {
                return CreateJsonResponse("""
                {
                  "data": [
                    {
                      "id": 1001,
                      "name": "Auto Downloaded",
                      "links": { "websiteUrl": "https://www.curseforge.com/minecraft/mc-mods/auto-downloaded" }
                    },
                    {
                      "id": 1002,
                      "name": "Manual Only",
                      "links": { "websiteUrl": "https://www.curseforge.com/minecraft/mc-mods/manual-only" }
                    }
                  ]
                }
                """);
            }

            throw new InvalidOperationException($"Unexpected CurseForge request: {request.Method} {request.RequestUri}");
        }));

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

    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
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
