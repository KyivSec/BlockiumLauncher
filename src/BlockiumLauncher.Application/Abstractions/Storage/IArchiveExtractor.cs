using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Shared.Primitives;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Storage;

public interface IArchiveExtractor
{
    Task<Result<Unit>> ExtractAsync(string ArchivePath, string DestinationPath, CancellationToken CancellationToken = default);
}