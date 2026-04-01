using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Catalog;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Shared.Results;
using Xunit;

namespace BlockiumLauncher.Tests.Application.Catalog;

public sealed class SearchCatalogUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_MergesSingleAndPluralFilters()
    {
        var catalogService = new CapturingContentCatalogService();
        var useCase = new SearchCatalogUseCase(catalogService);

        var result = await useCase.ExecuteAsync(new SearchCatalogRequest
        {
            Provider = CatalogProvider.Modrinth,
            ContentType = CatalogContentType.Modpack,
            GameVersion = "1.21.1",
            GameVersions = ["1.20.6"],
            Loader = "fabric",
            Loaders = ["quilt"],
            Categories = ["Adventure"]
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(catalogService.LastQuery);
        Assert.Equal(["1.20.6", "1.21.1"], catalogService.LastQuery!.GameVersions.OrderBy(static value => value).ToArray());
        Assert.Equal(["fabric", "quilt"], catalogService.LastQuery.Loaders.OrderBy(static value => value).ToArray());
        Assert.Null(catalogService.LastQuery.GameVersion);
        Assert.Null(catalogService.LastQuery.Loader);
    }

    private sealed class CapturingContentCatalogService : IContentCatalogService
    {
        public CatalogSearchQuery? LastQuery { get; private set; }

        public Task<Result<IReadOnlyList<CatalogProjectSummary>>> SearchAsync(
            CatalogSearchQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(Result<IReadOnlyList<CatalogProjectSummary>>.Success([]));
        }
    }
}
