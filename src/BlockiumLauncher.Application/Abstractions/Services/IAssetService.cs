using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Services;

public interface IAssetService
{
    Task<Result> DownloadAssetsAsync(LauncherInstance Instance, CancellationToken CancellationToken);
}
