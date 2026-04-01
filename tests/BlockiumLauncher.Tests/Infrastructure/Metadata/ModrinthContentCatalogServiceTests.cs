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

    [Fact]
    public async Task SearchAsync_BuildsMultiSelectFacets()
    {
        Uri? capturedUri = null;

        var httpClient = new FakeMetadataHttpClient(uri =>
        {
            capturedUri = uri;
            return Result<string>.Success("""{ "hits": [] }""");
        });

        var service = new ModrinthContentCatalogService(httpClient);

        var result = await service.SearchAsync(new CatalogSearchQuery
        {
            Provider = CatalogProvider.Modrinth,
            ContentType = CatalogContentType.Modpack,
            GameVersions = ["1.21.1", "1.20.6"],
            Loaders = ["fabric", "quilt"],
            Categories = ["adventure", "magic"]
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedUri);
        Assert.Contains("project_type%3Amodpack", capturedUri!.Query, StringComparison.Ordinal);
        Assert.Contains("versions%3A1.21.1", capturedUri.Query, StringComparison.Ordinal);
        Assert.Contains("versions%3A1.20.6", capturedUri.Query, StringComparison.Ordinal);
        Assert.Contains("categories%3Afabric", capturedUri.Query, StringComparison.Ordinal);
        Assert.Contains("categories%3Aquilt", capturedUri.Query, StringComparison.Ordinal);
        Assert.Contains("categories%3Aadventure", capturedUri.Query, StringComparison.Ordinal);
        Assert.Contains("categories%3Amagic", capturedUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetProjectDetailsAsync_ParsesMarkdownBody()
    {
        var httpClient = new FakeMetadataHttpClient(uri =>
        {
            Assert.EndsWith("/v2/project/example-modpack", uri.AbsoluteUri, StringComparison.Ordinal);

            return Result<string>.Success("""
            {
              "id": "example-modpack",
              "slug": "example-modpack",
              "title": "Example Modpack",
              "description": "Short summary",
              "body": "# Heading\n\nA longer markdown body.",
              "downloads": 144,
              "followers": 12,
              "published": "2026-03-01T00:00:00Z",
              "updated": "2026-03-10T00:00:00Z",
              "icon_url": "https://cdn.modrinth.com/example.png",
              "categories": ["adventure"],
              "game_versions": ["1.21.1"],
              "loaders": ["fabric"]
            }
            """);
        });

        var service = new ModrinthContentCatalogService(httpClient);
        var result = await service.GetProjectDetailsAsync(new CatalogProjectDetailsQuery
        {
            Provider = CatalogProvider.Modrinth,
            ContentType = CatalogContentType.Modpack,
            ProjectId = "example-modpack"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogDescriptionFormat.Markdown, result.Value.DescriptionFormat);
        Assert.Equal("# Heading\n\nA longer markdown body.", result.Value.DescriptionContent);
        Assert.Equal("Example Modpack", result.Value.Title);
    }

    [Fact]
    public async Task GetMetadataAsync_LoadsProviderFacets()
    {
        var httpClient = new FakeMetadataHttpClient(uri =>
        {
            if (uri.AbsoluteUri.EndsWith("/v2/tag/category", StringComparison.Ordinal))
            {
                return Result<string>.Success("""
                [
                  { "name": "adventure", "project_type": "modpack" },
                  { "name": "magic", "project_type": "modpack" },
                  { "name": "optimization", "project_type": "mod" }
                ]
                """);
            }

            if (uri.AbsoluteUri.EndsWith("/v2/tag/game_version", StringComparison.Ordinal))
            {
                return Result<string>.Success("""
                [
                  { "version": "1.21.1" },
                  { "version": "1.20.6" }
                ]
                """);
            }

            if (uri.AbsoluteUri.EndsWith("/v2/tag/loader", StringComparison.Ordinal))
            {
                return Result<string>.Success("""
                [
                  { "name": "fabric", "supported_project_types": ["modpack", "mod"] },
                  { "name": "quilt", "supported_project_types": ["modpack"] },
                  { "name": "bukkit", "supported_project_types": ["plugin"] }
                ]
                """);
            }

            throw new InvalidOperationException($"Unexpected request: {uri}");
        });

        var service = new ModrinthContentCatalogService(httpClient);
        var result = await service.GetMetadataAsync(new CatalogProviderMetadataQuery
        {
            Provider = CatalogProvider.Modrinth,
            ContentType = CatalogContentType.Modpack
        });

        Assert.True(result.IsSuccess);
        Assert.Contains(CatalogSearchSort.Downloads, result.Value.SortOptions);
        Assert.Contains("adventure", result.Value.Categories);
        Assert.DoesNotContain("optimization", result.Value.Categories);
        Assert.Contains("1.21.1", result.Value.GameVersions);
        Assert.Contains("fabric", result.Value.Loaders, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("bukkit", result.Value.Loaders, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetFilesAsync_ParsesProjectVersionsIntoCatalogFiles()
    {
        var httpClient = new FakeMetadataHttpClient(uri =>
        {
            Assert.EndsWith("/v2/project/example-pack/version", uri.AbsoluteUri, StringComparison.Ordinal);

            return Result<string>.Success("""
            [
              {
                "id": "version-2",
                "name": "Example Pack 2.0.0",
                "version_number": "2.0.0",
                "date_published": "2026-03-20T00:00:00Z",
                "loaders": ["fabric"],
                "game_versions": ["1.21.1"],
                "files": [
                  {
                    "filename": "example-pack-2.0.0.mrpack",
                    "url": "https://cdn.modrinth.com/example-pack-2.0.0.mrpack",
                    "primary": true,
                    "size": 2048,
                    "hashes": { "sha1": "ABC123" }
                  }
                ]
              },
              {
                "id": "version-1",
                "name": "Example Pack 1.0.0 Server",
                "version_number": "1.0.0",
                "date_published": "2026-03-18T00:00:00Z",
                "loaders": ["forge"],
                "game_versions": ["1.20.6"],
                "files": [
                  {
                    "filename": "example-pack-server.mrpack",
                    "url": "https://cdn.modrinth.com/example-pack-server.mrpack",
                    "primary": true,
                    "size": 1024,
                    "hashes": { "sha1": "DEF456" }
                  }
                ]
              }
            ]
            """);
        });

        var service = new ModrinthContentCatalogService(httpClient);
        var result = await service.GetFilesAsync(new CatalogFileQuery
        {
            Provider = CatalogProvider.Modrinth,
            ContentType = CatalogContentType.Modpack,
            ProjectId = "example-pack",
            GameVersion = "1.21.1",
            Loader = "fabric"
        });

        Assert.True(result.IsSuccess);
        var file = Assert.Single(result.Value);
        Assert.Equal("version-2", file.FileId);
        Assert.Equal("example-pack-2.0.0.mrpack", file.FileName);
        Assert.Equal("https://cdn.modrinth.com/example-pack-2.0.0.mrpack", file.DownloadUrl);
        Assert.Equal("ABC123", file.Sha1);
        Assert.Equal("https://modrinth.com/project/example-pack/version/version-2", file.FilePageUrl);
        Assert.Contains("1.21.1", file.GameVersions);
        Assert.Contains("fabric", file.Loaders, StringComparer.OrdinalIgnoreCase);
        Assert.False(file.IsServerPack);
    }

    [Fact]
    public async Task ResolveFileAsync_SelectsBestMatchingVersion()
    {
        var httpClient = new FakeMetadataHttpClient(uri =>
        {
            Assert.EndsWith("/v2/project/example-pack/version", uri.AbsoluteUri, StringComparison.Ordinal);

            return Result<string>.Success("""
            [
              {
                "id": "version-1",
                "name": "Example Pack 1.0.0 Server",
                "version_number": "1.0.0",
                "date_published": "2026-03-18T00:00:00Z",
                "loaders": ["fabric"],
                "game_versions": ["1.21.1"],
                "files": [
                  {
                    "filename": "example-pack-server.mrpack",
                    "url": "https://cdn.modrinth.com/example-pack-server.mrpack",
                    "primary": true,
                    "size": 1024,
                    "hashes": { "sha1": "DEF456" }
                  }
                ]
              },
              {
                "id": "version-2",
                "name": "Example Pack 2.0.0",
                "version_number": "2.0.0",
                "date_published": "2026-03-20T00:00:00Z",
                "loaders": ["fabric"],
                "game_versions": ["1.21.1"],
                "files": [
                  {
                    "filename": "example-pack-2.0.0.mrpack",
                    "url": "https://cdn.modrinth.com/example-pack-2.0.0.mrpack",
                    "primary": true,
                    "size": 2048,
                    "hashes": { "sha1": "ABC123" }
                  }
                ]
              }
            ]
            """);
        });

        var service = new ModrinthContentCatalogService(httpClient);
        var result = await service.ResolveFileAsync(new CatalogFileResolutionQuery
        {
            Provider = CatalogProvider.Modrinth,
            ContentType = CatalogContentType.Modpack,
            ProjectId = "example-pack",
            GameVersion = "1.21.1",
            Loader = "fabric"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("version-2", result.Value.FileId);
        Assert.Equal("example-pack-2.0.0.mrpack", result.Value.FileName);
        Assert.False(result.Value.IsServerPack);
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
