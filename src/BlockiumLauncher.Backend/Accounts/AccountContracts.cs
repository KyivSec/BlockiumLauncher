using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Auth
{
    public interface IMicrosoftAuthProvider
    {
        Task<Result<MicrosoftAuthResult>> SignInAsync(CancellationToken CancellationToken = default);
    }

    public sealed class MicrosoftAuthResult
    {
        public string Username { get; init; } = string.Empty;
        public string AccountIdentifier { get; init; } = string.Empty;
        public string RefreshToken { get; init; } = string.Empty;
    }
}

namespace BlockiumLauncher.Application.Abstractions.Security
{
    public interface ITokenStore
    {
        Task<Result> SaveRefreshTokenAsync(AccountId AccountId, string RefreshToken, CancellationToken CancellationToken = default);
        Task<Result<string>> GetRefreshTokenAsync(AccountId AccountId, CancellationToken CancellationToken = default);
        Task<Result> DeleteRefreshTokenAsync(AccountId AccountId, CancellationToken CancellationToken = default);
    }
}

namespace BlockiumLauncher.Application.Abstractions.Services
{
    using BlockiumLauncher.Domain.Entities;

    public interface IAuthenticationService
    {
        Task<Result<LauncherAccount>> CreateOfflineAccountAsync(string DisplayName, CancellationToken CancellationToken);
        Task<Result<LauncherAccount>> RefreshAsync(LauncherAccount Account, CancellationToken CancellationToken);
    }
}
