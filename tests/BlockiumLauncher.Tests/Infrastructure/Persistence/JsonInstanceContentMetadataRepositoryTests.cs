using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Infrastructure.Persistence.Json;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using BlockiumLauncher.Infrastructure.Persistence.Repositories;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Persistence;

public sealed class JsonInstanceContentMetadataRepositoryTests
{
    [Fact]
    public async Task RoundTripsMetadata_ByInstanceLocation()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var launcherPaths = new LauncherPaths(rootDirectory);
            var repository = new JsonInstanceContentMetadataRepository(launcherPaths, new JsonFileStore());
            var installLocation = Path.Combine(launcherPaths.InstancesDirectory, "roundtrip");

            Directory.CreateDirectory(installLocation);

            var metadata = new InstanceContentMetadata
            {
                IndexedAtUtc = DateTimeOffset.UtcNow,
                TotalPlaytimeSeconds = 123,
                LastLaunchPlaytimeSeconds = 45,
                Mods =
                [
                    new InstanceFileMetadata
                    {
                        Name = "example.jar",
                        RelativePath = "mods/example.jar",
                        AbsolutePath = Path.Combine(installLocation, "mods", "example.jar"),
                        SizeBytes = 99
                    }
                ]
            };

            await repository.SaveAsync(installLocation, metadata);
            var loadedMetadata = await repository.LoadAsync(installLocation);

            Assert.NotNull(loadedMetadata);
            Assert.Equal(123, loadedMetadata!.TotalPlaytimeSeconds);
            Assert.Equal(45, loadedMetadata.LastLaunchPlaytimeSeconds);
            Assert.Single(loadedMetadata.Mods);
            Assert.Equal("mods/example.jar", loadedMetadata.Mods[0].RelativePath);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }
}
