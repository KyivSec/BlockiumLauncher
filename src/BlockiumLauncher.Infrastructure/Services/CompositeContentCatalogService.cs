using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Services;

public sealed class CompositeContentCatalogService : IContentCatalogService
{
    private readonly IReadOnlyDictionary<CatalogProvider, IContentCatalogProvider> providers;

    public CompositeContentCatalogService(IEnumerable<IContentCatalogProvider> providers)
    {
        this.providers = providers?
            .GroupBy(provider => provider.Provider)
            .ToDictionary(group => group.Key, group => group.Last())
            ?? throw new ArgumentNullException(nameof(providers));
    }

    public Task<Result<IReadOnlyList<CatalogProjectSummary>>> SearchAsync(
        CatalogSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!providers.TryGetValue(query.Provider, out var provider))
        {
            return Task.FromResult(Result<IReadOnlyList<CatalogProjectSummary>>.Failure(
                new BlockiumLauncher.Shared.Errors.Error(
                    "Catalog.ProviderNotSupported",
                    $"The catalog provider '{query.Provider}' is not supported.")));
        }

        return provider.SearchAsync(query, cancellationToken);
    }
}
