using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.Abstractions.Repositories;

public interface IAccountRepository
{
    Task<IReadOnlyList<LauncherAccount>> ListAsync(CancellationToken CancellationToken);
    Task<LauncherAccount?> GetByIdAsync(AccountId AccountId, CancellationToken CancellationToken);
    Task<LauncherAccount?> GetDefaultAsync(CancellationToken CancellationToken);
    Task SaveAsync(LauncherAccount Account, CancellationToken CancellationToken);
    Task DeleteAsync(AccountId AccountId, CancellationToken CancellationToken);
}
