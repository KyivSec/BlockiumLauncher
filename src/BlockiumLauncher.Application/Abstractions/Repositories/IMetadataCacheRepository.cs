using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.Abstractions.Repositories;

public interface IMetadataCacheRepository
{
    Task<IReadOnlyList<VersionSummary>?> GetCachedVersionsAsync(CancellationToken CancellationToken);
    Task SaveCachedVersionsAsync(IReadOnlyList<VersionSummary> Versions, CancellationToken CancellationToken);

    Task<IReadOnlyList<LoaderVersionSummary>?> GetCachedLoaderVersionsAsync(
        LoaderType LoaderType,
        VersionId GameVersion,
        CancellationToken CancellationToken);

    Task SaveCachedLoaderVersionsAsync(
        LoaderType LoaderType,
        VersionId GameVersion,
        IReadOnlyList<LoaderVersionSummary> LoaderVersions,
        CancellationToken CancellationToken);
}
