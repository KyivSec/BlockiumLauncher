using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Services;

public interface IContentCatalogProvider
{
    CatalogProvider Provider { get; }

    Task<Result<IReadOnlyList<CatalogProjectSummary>>> SearchAsync(
        CatalogSearchQuery query,
        CancellationToken cancellationToken = default);
}
