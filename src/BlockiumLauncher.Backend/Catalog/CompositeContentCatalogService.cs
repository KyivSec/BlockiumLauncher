using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Domain.Enums;
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

public sealed class CompositeContentCatalogDetailsService : IContentCatalogDetailsService
{
    private readonly IReadOnlyDictionary<CatalogProvider, IContentCatalogDetailsProvider> providers;

    public CompositeContentCatalogDetailsService(IEnumerable<IContentCatalogDetailsProvider> providers)
    {
        this.providers = providers?
            .GroupBy(provider => provider.Provider)
            .ToDictionary(group => group.Key, group => group.Last())
            ?? throw new ArgumentNullException(nameof(providers));
    }

    public Task<Result<CatalogProjectDetails>> GetProjectDetailsAsync(
        CatalogProjectDetailsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!providers.TryGetValue(query.Provider, out var provider))
        {
            return Task.FromResult(Result<CatalogProjectDetails>.Failure(
                new BlockiumLauncher.Shared.Errors.Error(
                    "Catalog.ProviderNotSupported",
                    $"The catalog provider '{query.Provider}' does not expose project details.")));
        }

        return provider.GetProjectDetailsAsync(query, cancellationToken);
    }
}

public sealed class CompositeContentCatalogMetadataService : IContentCatalogMetadataService
{
    private readonly IReadOnlyDictionary<CatalogProvider, IContentCatalogMetadataProvider> providers;

    public CompositeContentCatalogMetadataService(IEnumerable<IContentCatalogMetadataProvider> providers)
    {
        this.providers = providers?
            .GroupBy(provider => provider.Provider)
            .ToDictionary(group => group.Key, group => group.Last())
            ?? throw new ArgumentNullException(nameof(providers));
    }

    public Task<Result<CatalogProviderMetadata>> GetMetadataAsync(
        CatalogProviderMetadataQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!providers.TryGetValue(query.Provider, out var provider))
        {
            return Task.FromResult(Result<CatalogProviderMetadata>.Failure(
                new BlockiumLauncher.Shared.Errors.Error(
                    "Catalog.ProviderNotSupported",
                    $"The catalog provider '{query.Provider}' does not expose metadata.")));
        }

        return provider.GetMetadataAsync(query, cancellationToken);
    }
}

public sealed class CompositeContentCatalogFileService : IContentCatalogFileService
{
    private readonly IReadOnlyDictionary<CatalogProvider, IContentCatalogFileProvider> providers;

    public CompositeContentCatalogFileService(IEnumerable<IContentCatalogFileProvider> providers)
    {
        this.providers = providers?
            .GroupBy(provider => provider.Provider)
            .ToDictionary(group => group.Key, group => group.Last())
            ?? throw new ArgumentNullException(nameof(providers));
    }

    public Task<Result<IReadOnlyList<CatalogFileSummary>>> GetFilesAsync(
        CatalogFileQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!providers.TryGetValue(query.Provider, out var provider))
        {
            return Task.FromResult(Result<IReadOnlyList<CatalogFileSummary>>.Failure(
                new BlockiumLauncher.Shared.Errors.Error(
                    "Catalog.ProviderNotSupported",
                    $"The catalog provider '{query.Provider}' does not expose file downloads.")));
        }

        return provider.GetFilesAsync(query, cancellationToken);
    }

    public Task<Result<CatalogFileSummary>> ResolveFileAsync(
        CatalogFileResolutionQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!providers.TryGetValue(query.Provider, out var provider))
        {
            return Task.FromResult(Result<CatalogFileSummary>.Failure(
                new BlockiumLauncher.Shared.Errors.Error(
                    "Catalog.ProviderNotSupported",
                    $"The catalog provider '{query.Provider}' does not expose file downloads.")));
        }

        return provider.ResolveFileAsync(query, cancellationToken);
    }

    public Task<Result<CatalogFileDetails>> GetFileDetailsAsync(
        CatalogFileDetailsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!providers.TryGetValue(query.Provider, out var provider))
        {
            return Task.FromResult(Result<CatalogFileDetails>.Failure(
                new BlockiumLauncher.Shared.Errors.Error(
                    "Catalog.ProviderNotSupported",
                    $"The catalog provider '{query.Provider}' does not expose file details.")));
        }

        return provider.GetFileDetailsAsync(query, cancellationToken);
    }
}
