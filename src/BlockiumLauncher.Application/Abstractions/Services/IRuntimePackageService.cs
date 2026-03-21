using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Services;

public interface IRuntimePackageService
{
    Task<Result<string>> ResolveRuntimePackageAsync(VersionId GameVersion, CancellationToken CancellationToken);
    Task<Result<string>> DownloadRuntimePackageAsync(string PackageId, string DestinationDirectory, CancellationToken CancellationToken);
}
