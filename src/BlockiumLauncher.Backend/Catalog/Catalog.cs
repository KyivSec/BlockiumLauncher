using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Catalog;

public static class CatalogErrors
{
    public static readonly Error InvalidRequest = new("Catalog.InvalidRequest", "The catalog request is invalid.");
}

public sealed class SearchCatalogRequest
{
    public CatalogProvider Provider { get; init; } = CatalogProvider.Modrinth;
    public CatalogContentType ContentType { get; init; }
    public string? Query { get; init; }
    public string? GameVersion { get; init; }
    public string? Loader { get; init; }
    public IReadOnlyList<string> Categories { get; init; } = [];
    public CatalogSearchSort Sort { get; init; } = CatalogSearchSort.Relevance;
    public int Limit { get; init; } = 20;
    public int Offset { get; init; }
}

public sealed class SearchCatalogUseCase
{
    private readonly IContentCatalogService contentCatalogService;

    public SearchCatalogUseCase(IContentCatalogService contentCatalogService)
    {
        this.contentCatalogService = contentCatalogService ?? throw new ArgumentNullException(nameof(contentCatalogService));
    }

    public Task<Result<IReadOnlyList<CatalogProjectSummary>>> ExecuteAsync(
        SearchCatalogRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.Limit <= 0 || request.Limit > 100 || request.Offset < 0)
        {
            return Task.FromResult(Result<IReadOnlyList<CatalogProjectSummary>>.Failure(CatalogErrors.InvalidRequest));
        }

        return contentCatalogService.SearchAsync(new CatalogSearchQuery
        {
            Provider = request.Provider,
            ContentType = request.ContentType,
            Query = request.Query,
            GameVersion = request.GameVersion,
            Loader = request.Loader,
            Categories = request.Categories,
            Sort = request.Sort,
            Limit = request.Limit,
            Offset = request.Offset
        }, cancellationToken);
    }
}
