using System.Net;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Backend.Catalog;
using BlockiumLauncher.Infrastructure.Metadata.Clients;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Metadata;

public sealed class CurseForgeContentCatalogServiceTests
{
    [Fact]
    public async Task SearchAsync_ResolvesClassAndParsesMods()
    {
        var handler = new FakeHttpMessageHandler((request, cancellationToken) =>
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains("/v1/categories?gameId=432&classesOnly=true", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "data": [
                            { "id": 6, "name": "Mods", "slug": "mc-mods", "isClass": true },
                            { "id": 4471, "name": "Modpacks", "slug": "modpacks", "isClass": true },
                            { "id": 12, "name": "Resource Packs", "slug": "texture-packs", "isClass": true },
                            { "id": 6552, "name": "Shaders", "slug": "shaders", "isClass": true }
                          ]
                        }
                        """)
                });
            }

            if (url.Contains("/v1/mods/search?", StringComparison.Ordinal))
            {
                Assert.Contains("gameId=432", url, StringComparison.Ordinal);
                Assert.Contains("classId=6", url, StringComparison.Ordinal);
                Assert.Contains("gameVersion=1.21.1", url, StringComparison.Ordinal);
                Assert.Contains("modLoaderType=6", url, StringComparison.Ordinal);

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "data": [
                            {
                              "id": 1001,
                              "name": "Example NeoForge Mod",
                              "slug": "example-neoforge-mod",
                              "summary": "An example project",
                              "downloadCount": 200,
                              "thumbsUpCount": 11,
                              "dateCreated": "2026-03-01T00:00:00Z",
                              "dateModified": "2026-03-10T00:00:00Z",
                              "links": { "websiteUrl": "https://www.curseforge.com/minecraft/mc-mods/example-neoforge-mod" },
                              "logo": { "url": "https://example.invalid/logo.png" },
                              "authors": [ { "name": "alice" } ],
                              "categories": [ { "name": "Magic" } ],
                              "latestFilesIndexes": [
                                { "gameVersion": "1.21.1", "modLoader": 6 }
                              ]
                            }
                          ]
                        }
                        """)
                });
            }

            throw new InvalidOperationException($"Unexpected request URL: {url}");
        });

        var service = new CurseForgeContentCatalogService(
            new HttpClient(handler),
            new CurseForgeOptions { ApiKey = "test-key" });
        var result = await service.SearchAsync(new CatalogSearchQuery
        {
            Provider = CatalogProvider.CurseForge,
            ContentType = CatalogContentType.Mod,
            GameVersion = "1.21.1",
            Loader = "neoforge",
            Query = "example"
        });

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value);
        Assert.Equal("1001", item.ProjectId);
        Assert.Equal("Example NeoForge Mod", item.Title);
        Assert.Equal("alice", item.Author);
        Assert.Contains("1.21.1", item.GameVersions);
        Assert.Contains("neoforge", item.Loaders, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_ReturnsFailure_WhenApiKeyIsMissing()
    {
        var service = new CurseForgeContentCatalogService(
            new HttpClient(new FakeHttpMessageHandler((request, cancellationToken) =>
            {
                throw new InvalidOperationException("HTTP should not be called when the API key is missing.");
            })),
            new CurseForgeOptions());

        var result = await service.SearchAsync(new CatalogSearchQuery
        {
            Provider = CatalogProvider.CurseForge,
            ContentType = CatalogContentType.Mod
        });

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.CurseForgeApiKeyMissing", result.Error.Code);
    }

    [Fact]
    public async Task SearchAsync_AppliesMultiSelectFiltersClientSide()
    {
        var handler = new FakeHttpMessageHandler((request, cancellationToken) =>
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains("/v1/categories?gameId=432&classesOnly=true", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "data": [
                            { "id": 4471, "name": "Modpacks", "slug": "modpacks", "isClass": true }
                          ]
                        }
                        """)
                });
            }

            if (url.Contains("/v1/categories?gameId=432&classId=4471", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "data": [
                            { "id": 1, "name": "Adventure", "slug": "adventure" },
                            { "id": 2, "name": "Kitchen Sink", "slug": "kitchen-sink" }
                          ]
                        }
                        """)
                });
            }

            if (url.Contains("/v1/mods/search?", StringComparison.Ordinal))
            {
                Assert.DoesNotContain("gameVersion=", url, StringComparison.Ordinal);
                Assert.DoesNotContain("modLoaderType=", url, StringComparison.Ordinal);

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "data": [
                            {
                              "id": 2001,
                              "name": "Matching Pack",
                              "slug": "matching-pack",
                              "summary": "Expected item",
                              "downloadCount": 200,
                              "thumbsUpCount": 11,
                              "dateCreated": "2026-03-01T00:00:00Z",
                              "dateModified": "2026-03-10T00:00:00Z",
                              "authors": [ { "name": "alice" } ],
                              "categories": [ { "name": "Adventure" } ],
                              "latestFilesIndexes": [
                                { "gameVersion": "1.21.1", "modLoader": 4 }
                              ]
                            },
                            {
                              "id": 2002,
                              "name": "Filtered Pack",
                              "slug": "filtered-pack",
                              "summary": "Should be removed",
                              "downloadCount": 50,
                              "thumbsUpCount": 3,
                              "dateCreated": "2026-03-01T00:00:00Z",
                              "dateModified": "2026-03-10T00:00:00Z",
                              "authors": [ { "name": "bob" } ],
                              "categories": [ { "name": "Kitchen Sink" } ],
                              "latestFilesIndexes": [
                                { "gameVersion": "1.18.2", "modLoader": 1 }
                              ]
                            }
                          ]
                        }
                        """)
                });
            }

            throw new InvalidOperationException($"Unexpected request URL: {url}");
        });

        var service = new CurseForgeContentCatalogService(
            new HttpClient(handler),
            new CurseForgeOptions { ApiKey = "test-key" });
        var result = await service.SearchAsync(new CatalogSearchQuery
        {
            Provider = CatalogProvider.CurseForge,
            ContentType = CatalogContentType.Modpack,
            GameVersions = ["1.21.1", "1.20.6"],
            Loaders = ["forge", "fabric"],
            Categories = ["Adventure"]
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        var item = Assert.Single(result.Value);
        Assert.Equal("2001", item.ProjectId);
    }

    [Fact]
    public async Task GetProjectDetailsAsync_ParsesHtmlDescription()
    {
        var handler = new FakeHttpMessageHandler((request, cancellationToken) =>
        {
            var url = request.RequestUri!.ToString();

            if (url.EndsWith("/v1/mods/1001", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "data": {
                        "id": 1001,
                        "name": "Example Modpack",
                        "slug": "example-modpack",
                        "summary": "Short summary",
                        "downloadCount": 321,
                        "thumbsUpCount": 22,
                        "dateCreated": "2026-03-01T00:00:00Z",
                        "dateModified": "2026-03-12T00:00:00Z",
                        "links": { "websiteUrl": "https://www.curseforge.com/minecraft/modpacks/example-modpack" },
                        "logo": { "url": "https://example.invalid/logo.png" },
                        "authors": [ { "name": "alice" } ],
                        "categories": [ { "name": "Adventure" } ],
                        "latestFilesIndexes": [
                          { "gameVersion": "1.21.1", "modLoader": 4 }
                        ]
                      }
                    }
                    """)
                });
            }

            if (url.EndsWith("/v1/mods/1001/description", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{ "data": "<h1>Overview</h1><p>Rich HTML body.</p>" }""")
                });
            }

            throw new InvalidOperationException($"Unexpected request URL: {url}");
        });

        var service = new CurseForgeContentCatalogService(
            new HttpClient(handler),
            new CurseForgeOptions { ApiKey = "test-key" });

        var result = await service.GetProjectDetailsAsync(new CatalogProjectDetailsQuery
        {
            Provider = CatalogProvider.CurseForge,
            ContentType = CatalogContentType.Modpack,
            ProjectId = "1001"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(CatalogDescriptionFormat.Html, result.Value.DescriptionFormat);
        Assert.Equal("<h1>Overview</h1><p>Rich HTML body.</p>", result.Value.DescriptionContent);
        Assert.Equal("Example Modpack", result.Value.Title);
    }

    [Fact]
    public async Task GetMetadataAsync_ParsesCategories()
    {
        var handler = new FakeHttpMessageHandler((request, cancellationToken) =>
        {
            var url = request.RequestUri!.ToString();

            if (url.Contains("/v1/categories?gameId=432&classesOnly=true", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "data": [
                        { "id": 4471, "name": "Modpacks", "slug": "modpacks", "isClass": true }
                      ]
                    }
                    """)
                });
            }

            if (url.Contains("/v1/categories?gameId=432&classId=4471", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "data": [
                        { "id": 1, "name": "Adventure" },
                        { "id": 2, "name": "Tech" }
                      ]
                    }
                    """)
                });
            }

            throw new InvalidOperationException($"Unexpected request URL: {url}");
        });

        var service = new CurseForgeContentCatalogService(
            new HttpClient(handler),
            new CurseForgeOptions { ApiKey = "test-key" });

        var result = await service.GetMetadataAsync(new CatalogProviderMetadataQuery
        {
            Provider = CatalogProvider.CurseForge,
            ContentType = CatalogContentType.Modpack
        });

        Assert.True(result.IsSuccess);
        Assert.Contains("Adventure", result.Value.Categories);
        Assert.Contains("forge", result.Value.Loaders, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(CatalogSearchSort.Updated, result.Value.SortOptions);
    }

    [Fact]
    public async Task GetFilesAsync_ParsesCurseForgeFileList()
    {
        var handler = new FakeHttpMessageHandler((request, cancellationToken) =>
        {
            Assert.Equal("test-key", request.Headers.GetValues("x-api-key").Single());

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "data": [
                    {
                      "id": 2001,
                      "displayName": "Example Mod 1.21.1",
                      "fileName": "example-mod.jar",
                      "fileLength": 12345,
                      "fileDate": "2026-03-18T00:00:00Z",
                      "downloadUrl": "https://downloads.invalid/example-mod.jar",
                      "gameVersions": [ "1.21.1", "NeoForge" ],
                      "hashes": [ { "value": "ABC123", "algo": 1 } ],
                      "isServerPack": false
                    }
                  ]
                }
                """)
            });
        });

        var service = new CurseForgeContentCatalogService(
            new HttpClient(handler),
            new CurseForgeOptions { ApiKey = "test-key" });

        var result = await service.GetFilesAsync(new CatalogFileQuery
        {
            Provider = CatalogProvider.CurseForge,
            ContentType = CatalogContentType.Mod,
            ProjectId = "1001",
            GameVersion = "1.21.1",
            Loader = "neoforge"
        });

        Assert.True(result.IsSuccess);
        var file = Assert.Single(result.Value);
        Assert.Equal("2001", file.FileId);
        Assert.Equal("example-mod.jar", file.FileName);
        Assert.Equal("https://downloads.invalid/example-mod.jar", file.DownloadUrl);
        Assert.Equal("ABC123", file.Sha1);
        Assert.Contains("1.21.1", file.GameVersions);
        Assert.Contains("neoforge", file.Loaders, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveFileAsync_FetchesDownloadUrl_WhenDirectFilePayloadDoesNotIncludeOne()
    {
        var handler = new FakeHttpMessageHandler((request, cancellationToken) =>
        {
            var url = request.RequestUri!.ToString();

            if (url.EndsWith("/v1/mods/1001/files/2001", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "data": {
                        "id": 2001,
                        "displayName": "Example Mod 1.21.1",
                        "fileName": "example-mod.jar",
                        "fileLength": 12345,
                        "fileDate": "2026-03-18T00:00:00Z",
                        "downloadUrl": null,
                        "gameVersions": [ "1.21.1", "NeoForge" ],
                        "hashes": [ { "value": "ABC123", "algo": 1 } ],
                        "isServerPack": false
                      }
                    }
                    """)
                });
            }

            if (url.EndsWith("/v1/mods/1001/files/2001/download-url", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{ "data": "https://downloads.invalid/example-mod.jar" }""")
                });
            }

            throw new InvalidOperationException($"Unexpected request URL: {url}");
        });

        var service = new CurseForgeContentCatalogService(
            new HttpClient(handler),
            new CurseForgeOptions { ApiKey = "test-key" });

        var result = await service.ResolveFileAsync(new CatalogFileResolutionQuery
        {
            Provider = CatalogProvider.CurseForge,
            ContentType = CatalogContentType.Mod,
            ProjectId = "1001",
            FileId = "2001"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("2001", result.Value.FileId);
        Assert.Equal("https://downloads.invalid/example-mod.jar", result.Value.DownloadUrl);
    }

    [Fact]
    public async Task ResolveFileAsync_ReturnsManualDownloadLink_WhenDownloadUrlIsForbidden()
    {
        var handler = new FakeHttpMessageHandler((request, cancellationToken) =>
        {
            var url = request.RequestUri!.ToString();

            if (url.EndsWith("/v1/mods/1001/files/2001", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "data": {
                        "id": 2001,
                        "displayName": "Blocked Mod",
                        "fileName": "blocked-mod.jar",
                        "fileLength": 444,
                        "fileDate": "2026-03-18T00:00:00Z",
                        "downloadUrl": null,
                        "gameVersions": [ "1.21.1", "NeoForge" ],
                        "hashes": [ { "value": "ABC123", "algo": 1 } ],
                        "isServerPack": false
                      }
                    }
                    """)
                });
            }

            if (url.EndsWith("/v1/mods/1001/files/2001/download-url", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("""{ "error": "distribution denied" }""")
                });
            }

            if (url.EndsWith("/v1/mods/1001", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "data": {
                        "id": 1001,
                        "slug": "blocked-mod",
                        "links": {
                          "websiteUrl": "https://www.curseforge.com/minecraft/mc-mods/blocked-mod"
                        }
                      }
                    }
                    """)
                });
            }

            throw new InvalidOperationException($"Unexpected request URL: {url}");
        });

        var service = new CurseForgeContentCatalogService(
            new HttpClient(handler),
            new CurseForgeOptions { ApiKey = "test-key" });

        var result = await service.ResolveFileAsync(new CatalogFileResolutionQuery
        {
            Provider = CatalogProvider.CurseForge,
            ContentType = CatalogContentType.Mod,
            ProjectId = "1001",
            FileId = "2001"
        });

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.RequiresManualDownload);
        Assert.Null(result.Value.DownloadUrl);
        Assert.Equal("https://www.curseforge.com/minecraft/mc-mods/blocked-mod", result.Value.ProjectUrl);
        Assert.Equal("https://www.curseforge.com/minecraft/mc-mods/blocked-mod/files/2001", result.Value.FilePageUrl);
    }
}
