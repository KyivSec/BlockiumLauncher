using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Catalog;

public sealed class GetCatalogProjectDetailsRequest
{
    public CatalogProvider Provider { get; init; } = CatalogProvider.Modrinth;
    public CatalogContentType ContentType { get; init; }
    public string ProjectId { get; init; } = string.Empty;
}

public sealed class GetCatalogProjectDetailsUseCase
{
    private readonly IContentCatalogDetailsService contentCatalogDetailsService;

    public GetCatalogProjectDetailsUseCase(IContentCatalogDetailsService contentCatalogDetailsService)
    {
        this.contentCatalogDetailsService = contentCatalogDetailsService ?? throw new ArgumentNullException(nameof(contentCatalogDetailsService));
    }

    public Task<Result<CatalogProjectDetails>> ExecuteAsync(
        GetCatalogProjectDetailsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ProjectId))
        {
            return Task.FromResult(Result<CatalogProjectDetails>.Failure(CatalogErrors.InvalidRequest));
        }

        return contentCatalogDetailsService.GetProjectDetailsAsync(new CatalogProjectDetailsQuery
        {
            Provider = request.Provider,
            ContentType = request.ContentType,
            ProjectId = request.ProjectId.Trim()
        }, cancellationToken);
    }
}

public sealed class GetCatalogProviderMetadataRequest
{
    public CatalogProvider Provider { get; init; } = CatalogProvider.Modrinth;
    public CatalogContentType ContentType { get; init; }
}

public sealed class GetCatalogProviderMetadataUseCase
{
    private readonly IContentCatalogMetadataService contentCatalogMetadataService;

    public GetCatalogProviderMetadataUseCase(IContentCatalogMetadataService contentCatalogMetadataService)
    {
        this.contentCatalogMetadataService = contentCatalogMetadataService ?? throw new ArgumentNullException(nameof(contentCatalogMetadataService));
    }

    public Task<Result<CatalogProviderMetadata>> ExecuteAsync(
        GetCatalogProviderMetadataRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Task.FromResult(Result<CatalogProviderMetadata>.Failure(CatalogErrors.InvalidRequest));
        }

        return contentCatalogMetadataService.GetMetadataAsync(new CatalogProviderMetadataQuery
        {
            Provider = request.Provider,
            ContentType = request.ContentType
        }, cancellationToken);
    }
}

public sealed class GetCatalogFileDetailsRequest
{
    public CatalogProvider Provider { get; init; } = CatalogProvider.Modrinth;
    public CatalogContentType ContentType { get; init; }
    public string ProjectId { get; init; } = string.Empty;
    public string FileId { get; init; } = string.Empty;
}

public sealed class GetCatalogFileDetailsUseCase
{
    private readonly IContentCatalogFileService contentCatalogFileService;

    public GetCatalogFileDetailsUseCase(IContentCatalogFileService contentCatalogFileService)
    {
        this.contentCatalogFileService = contentCatalogFileService ?? throw new ArgumentNullException(nameof(contentCatalogFileService));
    }

    public Task<Result<CatalogFileDetails>> ExecuteAsync(
        GetCatalogFileDetailsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.FileId))
        {
            return Task.FromResult(Result<CatalogFileDetails>.Failure(CatalogErrors.InvalidRequest));
        }

        return contentCatalogFileService.GetFileDetailsAsync(new CatalogFileDetailsQuery
        {
            Provider = request.Provider,
            ContentType = request.ContentType,
            ProjectId = request.ProjectId.Trim(),
            FileId = request.FileId.Trim()
        }, cancellationToken);
    }
}
