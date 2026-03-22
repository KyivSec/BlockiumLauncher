using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Security;

public interface ITokenStore
{
    Task<Result> SaveRefreshTokenAsync(AccountId AccountId, string RefreshToken, CancellationToken CancellationToken = default);
    Task<Result<string>> GetRefreshTokenAsync(AccountId AccountId, CancellationToken CancellationToken = default);
    Task<Result> DeleteRefreshTokenAsync(AccountId AccountId, CancellationToken CancellationToken = default);
}