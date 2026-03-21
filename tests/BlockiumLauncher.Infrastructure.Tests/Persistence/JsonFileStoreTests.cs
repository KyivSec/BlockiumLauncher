using BlockiumLauncher.Infrastructure.Persistence.Json;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Persistence;

public sealed class JsonFileStoreTests
{
    private sealed class TestModel
    {
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public async Task WriteAndReadOptional_RoundTrips()
    {
        var RootDirectory = Path.Combine(Path.GetTempPath(), "BlockiumLauncherTests", Guid.NewGuid().ToString("N"));
        var FilePath = Path.Combine(RootDirectory, "sample.json");
        var Store = new JsonFileStore();

        await Store.WriteAsync(FilePath, new TestModel { Name = "Test" }, CancellationToken.None);
        var Loaded = await Store.ReadOptionalAsync<TestModel>(FilePath, CancellationToken.None);

        Assert.NotNull(Loaded);
        Assert.Equal("Test", Loaded!.Name);
    }

    [Fact]
    public async Task MissingOptional_ReturnsNull()
    {
        var RootDirectory = Path.Combine(Path.GetTempPath(), "BlockiumLauncherTests", Guid.NewGuid().ToString("N"));
        var FilePath = Path.Combine(RootDirectory, "missing.json");
        var Store = new JsonFileStore();

        var Loaded = await Store.ReadOptionalAsync<TestModel>(FilePath, CancellationToken.None);

        Assert.Null(Loaded);
    }
}
