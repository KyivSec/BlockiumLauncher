using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Services;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Services;

public sealed class InstanceContentMetadataServiceTests
{
    [Fact]
    public async Task RecordLaunchAsync_AccumulatesPlaytime()
    {
        var repository = new FakeInstanceContentMetadataRepository();
        var service = new InstanceContentMetadataService(repository, new FakeInstanceContentIndexer());
        var instance = LauncherInstance.Create(
            InstanceId.New(),
            "Tracked",
            VersionId.Parse("1.20.1"),
            LoaderType.Vanilla,
            null,
            Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests", Path.GetRandomFileName()),
            DateTimeOffset.UtcNow,
            LaunchProfile.CreateDefault());

        var startedAtUtc = new DateTimeOffset(2026, 3, 24, 10, 0, 0, TimeSpan.Zero);
        var exitedAtUtc = startedAtUtc.AddMinutes(15);

        var metadata = await service.RecordLaunchAsync(instance, startedAtUtc, exitedAtUtc);

        Assert.Equal(900, metadata.TotalPlaytimeSeconds);
        Assert.Equal(900, metadata.LastLaunchPlaytimeSeconds);
        Assert.Equal(startedAtUtc, metadata.LastLaunchAtUtc);
    }

    private sealed class FakeInstanceContentMetadataRepository : IInstanceContentMetadataRepository
    {
        private readonly Dictionary<string, InstanceContentMetadata> items = new(StringComparer.OrdinalIgnoreCase);

        public Task<InstanceContentMetadata?> LoadAsync(string installLocation, CancellationToken cancellationToken = default)
        {
            items.TryGetValue(installLocation, out var metadata);
            return Task.FromResult(metadata);
        }

        public Task SaveAsync(string installLocation, InstanceContentMetadata metadata, CancellationToken cancellationToken = default)
        {
            items[installLocation] = metadata;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInstanceContentIndexer : IInstanceContentIndexer
    {
        public Task<InstanceContentMetadata> ScanAsync(string installLocation, InstanceContentMetadata? existingMetadata = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(existingMetadata ?? new InstanceContentMetadata());
        }
    }
}
