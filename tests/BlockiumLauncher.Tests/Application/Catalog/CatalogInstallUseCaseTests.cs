using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.UseCases.Catalog;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Downloads;
using BlockiumLauncher.Shared.Results;
using Xunit;

namespace BlockiumLauncher.Application.Tests.Catalog;

public sealed class CatalogInstallUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_DownloadsContentIntoInstanceAndAppliesSource()
    {
        var instanceRoot = Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(instanceRoot);

        try
        {
            var instance = LauncherInstance.Create(
                InstanceId.New(),
                "Installed",
                VersionId.Parse("1.21.1"),
                LoaderType.NeoForge,
                VersionId.Parse("21.0.167"),
                instanceRoot,
                DateTimeOffset.UtcNow,
                LaunchProfile.CreateDefault());

            instance.MarkInstalled();

            var metadataService = new FakeInstanceContentMetadataService();
            var useCase = new InstallCatalogContentUseCase(
                new FakeInstanceRepository(instance),
                new FakeContentCatalogFileService(),
                new FakeDownloader(),
                new FakeTempWorkspaceFactory(),
                metadataService);

            var result = await useCase.ExecuteAsync(new InstallCatalogContentRequest
            {
                Provider = CatalogProvider.CurseForge,
                ContentType = CatalogContentType.Mod,
                InstanceId = instance.InstanceId,
                ProjectId = "1001",
                FileId = "2001"
            });

            Assert.True(result.IsSuccess);
            Assert.Equal(Path.Combine(instanceRoot, "mods", "example-mod.jar"), result.Value.InstalledPath);
            Assert.True(File.Exists(result.Value.InstalledPath));
            Assert.Contains("mods/example-mod.jar", metadataService.AppliedSources.Keys, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(ContentOriginProvider.CurseForge, metadataService.AppliedSources["mods/example-mod.jar"].Provider);
            Assert.Equal("1001", metadataService.AppliedSources["mods/example-mod.jar"].ProjectId);
            Assert.Equal("2001", metadataService.AppliedSources["mods/example-mod.jar"].FileId);
        }
        finally
        {
            if (Directory.Exists(instanceRoot))
            {
                Directory.Delete(instanceRoot, true);
            }
        }
    }

    private sealed class FakeInstanceRepository : IInstanceRepository
    {
        private readonly LauncherInstance instance;

        public FakeInstanceRepository(LauncherInstance instance)
        {
            this.instance = instance;
        }

        public Task<IReadOnlyList<LauncherInstance>> ListAsync(CancellationToken CancellationToken)
            => Task.FromResult<IReadOnlyList<LauncherInstance>>([instance]);

        public Task<LauncherInstance?> GetByIdAsync(InstanceId InstanceId, CancellationToken CancellationToken)
            => Task.FromResult<LauncherInstance?>(InstanceId.Equals(instance.InstanceId) ? instance : null);

        public Task<LauncherInstance?> GetByNameAsync(string Name, CancellationToken CancellationToken)
            => Task.FromResult<LauncherInstance?>(string.Equals(Name, instance.Name, StringComparison.OrdinalIgnoreCase) ? instance : null);

        public Task SaveAsync(LauncherInstance Instance, CancellationToken CancellationToken)
            => Task.CompletedTask;

        public Task DeleteAsync(InstanceId InstanceId, CancellationToken CancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FakeContentCatalogFileService : IContentCatalogFileService
    {
        public Task<Result<IReadOnlyList<CatalogFileSummary>>> GetFilesAsync(CatalogFileQuery query, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<CatalogFileSummary>> ResolveFileAsync(CatalogFileResolutionQuery query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<CatalogFileSummary>.Success(new CatalogFileSummary
            {
                Provider = CatalogProvider.CurseForge,
                ContentType = CatalogContentType.Mod,
                ProjectId = query.ProjectId,
                FileId = query.FileId ?? "2001",
                DisplayName = "Example Mod",
                FileName = "example-mod.jar",
                DownloadUrl = "https://downloads.invalid/example-mod.jar",
                Sha1 = null
            }));
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

            File.WriteAllText(Request.DestinationPath, "jar-bytes");
            return Task.FromResult(Result<DownloadResult>.Success(new DownloadResult(Request.DestinationPath, 9)));
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

    private sealed class FakeInstanceContentMetadataService : IInstanceContentMetadataService
    {
        public Dictionary<string, ContentSourceMetadata> AppliedSources { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<InstanceContentMetadata?> GetAsync(LauncherInstance instance, bool reindexIfMissing = false, CancellationToken cancellationToken = default)
            => Task.FromResult<InstanceContentMetadata?>(null);

        public Task<InstanceContentMetadata> ReindexAsync(LauncherInstance instance, CancellationToken cancellationToken = default)
            => Task.FromResult(new InstanceContentMetadata());

        public Task<InstanceContentMetadata> SetModEnabledAsync(LauncherInstance instance, string modReference, bool enabled, CancellationToken cancellationToken = default)
            => Task.FromResult(new InstanceContentMetadata());

        public Task<InstanceContentMetadata> RecordLaunchAsync(LauncherInstance instance, DateTimeOffset startedAtUtc, DateTimeOffset exitedAtUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(new InstanceContentMetadata());

        public Task<InstanceContentMetadata> ApplySourcesAsync(
            LauncherInstance instance,
            IReadOnlyDictionary<string, ContentSourceMetadata> sourcesByRelativePath,
            CancellationToken cancellationToken = default)
        {
            foreach (var pair in sourcesByRelativePath)
            {
                AppliedSources[pair.Key] = pair.Value;
            }

            return Task.FromResult(new InstanceContentMetadata());
        }
    }
}
