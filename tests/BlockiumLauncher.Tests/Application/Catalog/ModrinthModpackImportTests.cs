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

            var importUseCase = new ImportCatalogModpackUseCase(
                new FakeContentCatalogFileService(),
                new FakeDownloader(),
                tempWorkspaceFactory,
                new FakeArchiveExtractor(),
                installUseCase,
                repository,
                metadataService,
                new InMemoryManualDownloadStateStore());

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

    private sealed class FakeContentCatalogFileService : IContentCatalogFileService
    {
        public Task<Result<IReadOnlyList<CatalogFileSummary>>> GetFilesAsync(CatalogFileQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

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
}
