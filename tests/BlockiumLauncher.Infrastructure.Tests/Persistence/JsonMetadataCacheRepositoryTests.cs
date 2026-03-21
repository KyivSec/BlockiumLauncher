using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Persistence.Json;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using BlockiumLauncher.Infrastructure.Persistence.Repositories;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Persistence;

public sealed class JsonMetadataCacheRepositoryTests
{
    [Fact]
    public async Task VersionsCache_RoundTrips()
    {
        var RootDirectory = Path.Combine(Path.GetTempPath(), "BlockiumLauncherTests", Guid.NewGuid().ToString("N"));
        var Paths = new LauncherPaths(RootDirectory);
        var Store = new JsonFileStore();
        var Repository = new JsonMetadataCacheRepository(Paths, Store);

        var Versions = new[]
        {
            new VersionSummary(new VersionId("1.21.1"), "1.21.1", true, DateTimeOffset.UtcNow)
        };

        await Repository.SaveCachedVersionsAsync(Versions, CancellationToken.None);
        var Loaded = await Repository.GetCachedVersionsAsync(CancellationToken.None);

        Assert.NotNull(Loaded);
        Assert.Single(Loaded!);
        Assert.Equal("1.21.1", Loaded[0].VersionId.ToString());
    }

    [Fact]
    public async Task MissingVersionsCache_ReturnsNull()
    {
        var RootDirectory = Path.Combine(Path.GetTempPath(), "BlockiumLauncherTests", Guid.NewGuid().ToString("N"));
        var Paths = new LauncherPaths(RootDirectory);
        var Store = new JsonFileStore();
        var Repository = new JsonMetadataCacheRepository(Paths, Store);

        var Loaded = await Repository.GetCachedVersionsAsync(CancellationToken.None);

        Assert.Null(Loaded);
    }

    [Fact]
    public async Task CorruptVersionsCache_ReturnsNull()
    {
        var RootDirectory = Path.Combine(Path.GetTempPath(), "BlockiumLauncherTests", Guid.NewGuid().ToString("N"));
        var Paths = new LauncherPaths(RootDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(Paths.VersionsCacheFilePath)!);
        await File.WriteAllTextAsync(Paths.VersionsCacheFilePath, "{ bad json }");

        var Store = new JsonFileStore();
        var Repository = new JsonMetadataCacheRepository(Paths, Store);

        var Loaded = await Repository.GetCachedVersionsAsync(CancellationToken.None);

        Assert.Null(Loaded);
    }

    [Fact]
    public async Task LoaderCache_RoundTrips()
    {
        var RootDirectory = Path.Combine(Path.GetTempPath(), "BlockiumLauncherTests", Guid.NewGuid().ToString("N"));
        var Paths = new LauncherPaths(RootDirectory);
        var Store = new JsonFileStore();
        var Repository = new JsonMetadataCacheRepository(Paths, Store);

        var Versions = new[]
        {
            new LoaderVersionSummary(LoaderType.Forge, new VersionId("1.21.1"), "47.3.0", true)
        };

        await Repository.SaveCachedLoaderVersionsAsync(LoaderType.Forge, new VersionId("1.21.1"), Versions, CancellationToken.None);
        var Loaded = await Repository.GetCachedLoaderVersionsAsync(LoaderType.Forge, new VersionId("1.21.1"), CancellationToken.None);

        Assert.NotNull(Loaded);
        Assert.Single(Loaded!);
        Assert.Equal("47.3.0", Loaded[0].LoaderVersion);
    }
}
