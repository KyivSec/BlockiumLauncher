using System.Net;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Metadata;
using BlockiumLauncher.Infrastructure.Metadata.Clients;
using BlockiumLauncher.Infrastructure.Persistence.Json;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using BlockiumLauncher.Infrastructure.Persistence.Repositories;
using BlockiumLauncher.Infrastructure.Services;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Metadata;

public sealed class CachedLoaderMetadataServiceTests
{
    [Fact]
    public async Task GetLoaderVersionsAsync_ForFabric_UsesRemoteAndCaches()
    {
        var RootDirectory = Path.Combine(Path.GetTempPath(), "BlockiumLauncherTests", Guid.NewGuid().ToString("N"));
        var Paths = new LauncherPaths(RootDirectory);
        var Store = new JsonFileStore();
        var Repository = new JsonMetadataCacheRepository(Paths, Store);

        var Json = """
        [
          {
            "loader": {
              "version": "0.16.10",
              "stable": true
            }
          }
        ]
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

        var Service = new CachedLoaderMetadataService(
            Repository,
            Paths,
            new FabricMetadataClient(MetadataHttpClient),
            new QuiltMetadataClient(MetadataHttpClient),
            new ForgeMetadataClient(MetadataHttpClient),
            new NeoForgeMetadataClient(MetadataHttpClient),
            new MetadataCachePolicy());

        var Result = await Service.GetLoaderVersionsAsync(
            LoaderType.Fabric,
            new VersionId("1.21.1"),
            CancellationToken.None);

        Assert.True(Result.IsSuccess);
        Assert.Single(Result.Value);
        Assert.Equal("0.16.10", Result.Value[0].LoaderVersion);
    }
}
