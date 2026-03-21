using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Services;

public interface ILoaderMetadataService
{
    Task<Result<IReadOnlyList<LoaderVersionSummary>>> GetLoaderVersionsAsync(
        LoaderType LoaderType,
        VersionId GameVersion,
        CancellationToken CancellationToken);
}
