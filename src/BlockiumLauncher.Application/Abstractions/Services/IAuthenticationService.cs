using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Services;

public interface IAuthenticationService
{
    Task<Result<LauncherAccount>> CreateOfflineAccountAsync(string DisplayName, CancellationToken CancellationToken);
    Task<Result<LauncherAccount>> RefreshAsync(LauncherAccount Account, CancellationToken CancellationToken);
}
