using System.Net;
using BlockiumLauncher.Infrastructure.Metadata;
using BlockiumLauncher.Infrastructure.Metadata.Clients;
using BlockiumLauncher.Infrastructure.Persistence.Json;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using BlockiumLauncher.Infrastructure.Persistence.Repositories;
using BlockiumLauncher.Infrastructure.Services;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Metadata;

public sealed class CachedVersionManifestServiceTests
{
    [Fact]
    public async Task GetAvailableVersionsAsync_UsesRemoteAndCaches()
    {
        var RootDirectory = Path.Combine(Path.GetTempPath(), "BlockiumLauncherTests", Guid.NewGuid().ToString("N"));
        var Paths = new LauncherPaths(RootDirectory);
        var Store = new JsonFileStore();
        var Repository = new JsonMetadataCacheRepository(Paths, Store);

        var Json = """
        {
          "versions": [
            {
              "id": "1.21.1",
              "type": "release",
              "releaseTime": "2024-08-08T00:00:00+00:00"
            }
          ]
        }
        """;

        var Handler = new FakeHttpMessageHandler((Request, CancellationToken) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Json)
            });
        });

        var HttpClient = new HttpClient(Handler);
        var MetadataHttpClient = new MetadataHttpClient(HttpClient, new MetadataHttpOptions());
        var MojangClient = new MojangVersionManifestClient(MetadataHttpClient);
        var Service = new CachedVersionManifestService(
            Repository,
            Paths,
            MojangClient,
            new MetadataCachePolicy());

        var Result = await Service.GetAvailableVersionsAsync(CancellationToken.None);

        Assert.True(Result.IsSuccess);
        Assert.Single(Result.Value);

        var Cached = await Repository.GetCachedVersionsAsync(CancellationToken.None);
        Assert.NotNull(Cached);
        Assert.Single(Cached!);
    }
}
