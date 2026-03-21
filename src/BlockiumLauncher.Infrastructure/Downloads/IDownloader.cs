using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Downloads;

public interface IDownloader
{
    Task<Result<DownloadResult>> DownloadAsync(DownloadRequest Request, CancellationToken CancellationToken);
}
