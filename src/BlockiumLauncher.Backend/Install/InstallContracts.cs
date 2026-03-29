using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Services;

public interface IAssetService
{
    Task<Result> DownloadAssetsAsync(LauncherInstance Instance, CancellationToken CancellationToken);
}

public interface ILibraryService
{
    Task<Result> DownloadLibrariesAsync(LauncherInstance Instance, CancellationToken CancellationToken);
}

public interface ILoaderInstaller
{
    Task<Result> InstallAsync(LauncherInstance Instance, CancellationToken CancellationToken);
}

public interface IRuntimePackageService
{
    Task<Result<string>> ResolveRuntimePackageAsync(VersionId GameVersion, CancellationToken CancellationToken);
    Task<Result<string>> DownloadRuntimePackageAsync(string PackageId, string DestinationDirectory, CancellationToken CancellationToken);
}
