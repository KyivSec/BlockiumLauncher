using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Infrastructure.Metadata;
using BlockiumLauncher.Infrastructure.Metadata.Clients;
using BlockiumLauncher.Shared.Results;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Metadata;

public sealed class ModrinthContentCatalogServiceTests
{
    [Fact]
    public async Task SearchAsync_BuildsQueryAndParsesHits()
    {
        Uri? capturedUri = null;

        var httpClient = new FakeMetadataHttpClient(uri =>
        {
            capturedUri = uri;

            return Result<string>.Success("""
            {
              "hits": [
                {
                  "project_id": "abc123",
                  "slug": "example-mod",
                  "title": "Example Mod",
                  "description": "Example description",
                  "author": "alice",
                  "downloads": 42,
                  "follows": 7,
                  "date_created": "2026-03-01T00:00:00Z",
                  "date_modified": "2026-03-10T00:00:00Z",
                  "icon_url": "https://cdn.modrinth.com/example.png",
                  "display_categories": ["magic", "utility"],
                  "categories": ["neoforge", "magic"],
                  "versions": ["1.21.1"]
                }
              ]
            }
            """);
        });

        var service = new ModrinthContentCatalogService(httpClient);

        var result = await service.SearchAsync(new CatalogSearchQuery
        {
            Provider = CatalogProvider.Modrinth,
            ContentType = CatalogContentType.Mod,
            Query = "example",
            Loader = "neoforge",
            GameVersion = "1.21.1",
            Sort = CatalogSearchSort.Downloads,
            Limit = 5,
            Offset = 2
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedUri);
        Assert.Contains("limit=5", capturedUri!.Query, StringComparison.Ordinal);
        Assert.Contains("offset=2", capturedUri.Query, StringComparison.Ordinal);
        Assert.Contains("index=downloads", capturedUri.Query, StringComparison.Ordinal);
        Assert.Contains("query=example", capturedUri.Query, StringComparison.Ordinal);
        Assert.Contains("project_type%3Amod", capturedUri.Query, StringComparison.Ordinal);
        Assert.Contains("categories%3Aneoforge", capturedUri.Query, StringComparison.Ordinal);
        Assert.Contains("versions%3A1.21.1", capturedUri.Query, StringComparison.Ordinal);

        var item = Assert.Single(result.Value);
        Assert.Equal("abc123", item.ProjectId);
        Assert.Equal("Example Mod", item.Title);
        Assert.Equal("example-mod", item.Slug);
        Assert.Equal(42, item.Downloads);
        Assert.Contains("neoforge", item.Loaders, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("1.21.1", item.GameVersions, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("https://modrinth.com/project/example-mod", item.ProjectUrl);
    }

    private sealed class FakeMetadataHttpClient : IMetadataHttpClient
    {
        private readonly Func<Uri, Result<string>> handler;

        public FakeMetadataHttpClient(Func<Uri, Result<string>> handler)
        {
            this.handler = handler;
        }

        public Task<Result<string>> GetStringAsync(Uri Uri, CancellationToken CancellationToken)
        {
            return Task.FromResult(handler(Uri));
        }

        public Task<Result<Stream>> GetStreamAsync(Uri Uri, CancellationToken CancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
