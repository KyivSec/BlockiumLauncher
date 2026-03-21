using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Services;

public interface ILibraryService
{
    Task<Result> DownloadLibrariesAsync(LauncherInstance Instance, CancellationToken CancellationToken);
}
