using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Metadata;

public interface IMetadataHttpClient
{
    Task<Result<string>> GetStringAsync(Uri Uri, CancellationToken CancellationToken);
    Task<Result<Stream>> GetStreamAsync(Uri Uri, CancellationToken CancellationToken);
}
