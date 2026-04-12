using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Application.UseCases.Catalog;
using BlockiumLauncher.Contracts.Operations;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Services;

public interface IContentCatalogProvider
{
    CatalogProvider Provider { get; }

    Task<Result<IReadOnlyList<CatalogProjectSummary>>> SearchAsync(
        CatalogSearchQuery query,
        CancellationToken cancellationToken = default);
}

public interface IContentCatalogService
{
    Task<Result<IReadOnlyList<CatalogProjectSummary>>> SearchAsync(
        CatalogSearchQuery query,
        CancellationToken cancellationToken = default);
}

public interface IContentCatalogDetailsProvider
{
    CatalogProvider Provider { get; }

    Task<Result<CatalogProjectDetails>> GetProjectDetailsAsync(
        CatalogProjectDetailsQuery query,
        CancellationToken cancellationToken = default);
}

public interface IContentCatalogDetailsService
{
    Task<Result<CatalogProjectDetails>> GetProjectDetailsAsync(
        CatalogProjectDetailsQuery query,
        CancellationToken cancellationToken = default);
}

public interface IContentCatalogMetadataProvider
{
    CatalogProvider Provider { get; }

    Task<Result<CatalogProviderMetadata>> GetMetadataAsync(
        CatalogProviderMetadataQuery query,
        CancellationToken cancellationToken = default);
}

public interface IContentCatalogMetadataService
{
    Task<Result<CatalogProviderMetadata>> GetMetadataAsync(
        CatalogProviderMetadataQuery query,
        CancellationToken cancellationToken = default);
}

public interface IContentCatalogFileProvider
{
    CatalogProvider Provider { get; }

    Task<Result<IReadOnlyList<CatalogFileSummary>>> GetFilesAsync(
        CatalogFileQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<CatalogFileSummary>> ResolveFileAsync(
        CatalogFileResolutionQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<CatalogFileDetails>> GetFileDetailsAsync(
        CatalogFileDetailsQuery query,
        CancellationToken cancellationToken = default);
}

public interface IContentCatalogFileService
{
    Task<Result<IReadOnlyList<CatalogFileSummary>>> GetFilesAsync(
        CatalogFileQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<CatalogFileSummary>> ResolveFileAsync(
        CatalogFileResolutionQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<CatalogFileDetails>> GetFileDetailsAsync(
        CatalogFileDetailsQuery query,
        CancellationToken cancellationToken = default);
}

public interface IManualDownloadStateStore
{
    Task<PendingManualDownloadsState?> LoadAsync(string installLocation, CancellationToken cancellationToken = default);
    Task SaveAsync(string installLocation, PendingManualDownloadsState state, CancellationToken cancellationToken = default);
    Task DeleteAsync(string installLocation, CancellationToken cancellationToken = default);
}

public interface IOperationEventSink
{
    Task PublishAsync(OperationEventDto Event, CancellationToken CancellationToken);
}
