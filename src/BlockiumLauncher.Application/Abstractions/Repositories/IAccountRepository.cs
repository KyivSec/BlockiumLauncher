using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.Abstractions.Repositories;

public interface IAccountRepository
{
    Task<IReadOnlyList<LauncherAccount>> ListAsync(CancellationToken CancellationToken = default);
    Task<LauncherAccount?> GetByIdAsync(AccountId AccountId, CancellationToken CancellationToken = default);
    Task<LauncherAccount?> GetDefaultAsync(CancellationToken CancellationToken = default);
    Task SaveAsync(LauncherAccount Account, CancellationToken CancellationToken = default);
    Task DeleteAsync(AccountId AccountId, CancellationToken CancellationToken = default);
}