using System.Net;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Infrastructure.Metadata.Clients;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Metadata;

public sealed class CurseForgeContentCatalogServiceTests
{
    [Fact]
    public async Task SearchAsync_ResolvesClassAndParsesMods()
    {
        var previousApiKey = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
        Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", "test-key");

        try
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

            var service = new CurseForgeContentCatalogService(new HttpClient(handler));
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
        finally
        {
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", previousApiKey);
        }
    }
}
