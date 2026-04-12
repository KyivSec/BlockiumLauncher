using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Application.UseCases.Instances;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using Xunit;

namespace BlockiumLauncher.Application.Tests.Instances;

public sealed class ListInstanceBrowserSummariesUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsBrowserSummariesFromRepositoryAndMetadata()
    {
        var metadataIconPath = Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests", Path.GetRandomFileName() + ".png");
        var metadataIconDirectory = Path.GetDirectoryName(metadataIconPath);
        if (!string.IsNullOrWhiteSpace(metadataIconDirectory))
        {
            Directory.CreateDirectory(metadataIconDirectory);
        }

        await File.WriteAllTextAsync(metadataIconPath, "icon");

        var installed = LauncherInstance.Create(
            InstanceId.New(),
            "Adventure Pack",
            VersionId.Parse("1.21.1"),
            LoaderType.Fabric,
            VersionId.Parse("0.16.9"),
            Path.Combine(Path.GetTempPath(), "Adventure-Pack"),
            DateTimeOffset.UtcNow.AddDays(-10),
            LaunchProfile.CreateDefault(),
            @"C:\Icons\adventure.png");
        installed.MarkInstalled();
        installed.RecordLaunch(DateTimeOffset.UtcNow.AddDays(-1));

        var deleted = LauncherInstance.Create(
            InstanceId.New(),
            "Old Pack",
            VersionId.Parse("1.20.4"),
            LoaderType.Vanilla,
            null,
            Path.Combine(Path.GetTempPath(), "Old-Pack"),
            DateTimeOffset.UtcNow.AddDays(-40),
            LaunchProfile.CreateDefault());
        deleted.MarkDeleted();

        var useCase = new ListInstanceBrowserSummariesUseCase(
            new FakeInstanceRepository(installed, deleted),
            new FakeMetadataService(
                installed.InstallLocation,
                new InstanceContentMetadata
                {
                    IconPath = metadataIconPath,
                    TotalPlaytimeSeconds = 7200,
                    LastLaunchAtUtc = DateTimeOffset.UtcNow.AddDays(-1)
                }));

        var result = await useCase.ExecuteAsync(new ListInstancesRequest());

        Assert.True(result.IsSuccess);
        var summary = Assert.Single(result.Value);
        Assert.Equal(installed.InstanceId, summary.InstanceId);
        Assert.Equal("Adventure Pack", summary.Name);
        Assert.Equal("1.21.1", summary.GameVersion);
        Assert.Equal(LoaderType.Fabric, summary.LoaderType);
        Assert.Equal(7200, summary.TotalPlaytimeSeconds);
        Assert.Equal(metadataIconPath, summary.IconPath);
    }

    private sealed class FakeInstanceRepository : IInstanceRepository
    {
        private readonly IReadOnlyList<LauncherInstance> instances;

        public FakeInstanceRepository(params LauncherInstance[] instances)
        {
            this.instances = instances;
        }

        public Task<IReadOnlyList<LauncherInstance>> ListAsync(CancellationToken CancellationToken)
            => Task.FromResult(instances);

        public Task<LauncherInstance?> GetByIdAsync(InstanceId InstanceId, CancellationToken CancellationToken)
            => Task.FromResult(instances.FirstOrDefault(instance => instance.InstanceId.Equals(InstanceId)));

        public Task<LauncherInstance?> GetByNameAsync(string Name, CancellationToken CancellationToken)
            => Task.FromResult(instances.FirstOrDefault(instance => string.Equals(instance.Name, Name, StringComparison.OrdinalIgnoreCase)));

        public Task SaveAsync(LauncherInstance Instance, CancellationToken CancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(InstanceId InstanceId, CancellationToken CancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeMetadataService : IInstanceContentMetadataService
    {
        private readonly string installLocation;
        private readonly InstanceContentMetadata metadata;

        public FakeMetadataService(string installLocation, InstanceContentMetadata metadata)
        {
            this.installLocation = installLocation;
            this.metadata = metadata;
        }

        public Task<InstanceContentMetadata?> GetAsync(LauncherInstance instance, bool reindexIfMissing = false, CancellationToken cancellationToken = default)
            => Task.FromResult<InstanceContentMetadata?>(string.Equals(instance.InstallLocation, installLocation, StringComparison.OrdinalIgnoreCase) ? metadata : null);

        public Task<InstanceContentMetadata> ReindexAsync(LauncherInstance instance, CancellationToken cancellationToken = default)
            => Task.FromResult(metadata);

        public Task<InstanceContentMetadata> SetModEnabledAsync(LauncherInstance instance, string modReference, bool enabled, CancellationToken cancellationToken = default)
            => Task.FromResult(metadata);

        public Task<InstanceContentMetadata> SetContentEnabledAsync(LauncherInstance instance, InstanceContentCategory category, string contentReference, bool enabled, CancellationToken cancellationToken = default)
            => Task.FromResult(metadata);

        public Task<InstanceContentMetadata> DeleteContentAsync(LauncherInstance instance, InstanceContentCategory category, string contentReference, CancellationToken cancellationToken = default)
            => Task.FromResult(metadata);

        public Task<InstanceContentMetadata> RecordLaunchAsync(LauncherInstance instance, DateTimeOffset startedAtUtc, DateTimeOffset exitedAtUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(metadata);

        public Task<InstanceContentMetadata> ApplySourcesAsync(LauncherInstance instance, IReadOnlyDictionary<string, ContentSourceMetadata> sourcesByRelativePath, CancellationToken cancellationToken = default)
            => Task.FromResult(metadata);
    }
}
