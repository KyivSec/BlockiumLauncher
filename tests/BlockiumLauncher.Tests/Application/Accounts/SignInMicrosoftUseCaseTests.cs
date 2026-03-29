using BlockiumLauncher.Application.Abstractions.Auth;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Security;
using BlockiumLauncher.Application.UseCases.Accounts;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;
using Xunit;

namespace BlockiumLauncher.Application.Tests.Accounts;

public sealed class SignInMicrosoftUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_AddsMicrosoftAccount_WhenProviderSucceeds()
    {
        var Repository = new FakeAccountRepository();
        var TokenStore = new FakeTokenStore();
        var AddAccountUseCase = new AddAccountUseCase(Repository, TokenStore);
        var Provider = new FakeMicrosoftAuthProvider(Result<MicrosoftAuthResult>.Success(new MicrosoftAuthResult
        {
            Username = "Steve",
            AccountIdentifier = "xuid-1",
            RefreshToken = "refresh-1"
        }));

        var UseCase = new SignInMicrosoftUseCase(Provider, AddAccountUseCase);

        var Result = await UseCase.ExecuteAsync();

        Assert.True(Result.IsSuccess);
        Assert.Equal(AccountProvider.Microsoft, Result.Value.Provider);
        Assert.Equal("Steve", Result.Value.Username);
        Assert.Equal("xuid-1", Result.Value.AccountIdentifier);

        var Accounts = await Repository.ListAsync();
        Assert.Single(Accounts);
        Assert.Equal(AccountProvider.Microsoft, Accounts[0].Provider);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenProviderFails()
    {
        var Repository = new FakeAccountRepository();
        var TokenStore = new FakeTokenStore();
        var AddAccountUseCase = new AddAccountUseCase(Repository, TokenStore);
        var Provider = new FakeMicrosoftAuthProvider(Result<MicrosoftAuthResult>.Failure(AccountErrors.PersistenceFailed));

        var UseCase = new SignInMicrosoftUseCase(Provider, AddAccountUseCase);

        var Result = await UseCase.ExecuteAsync();

        Assert.True(Result.IsFailure);

        var Accounts = await Repository.ListAsync();
        Assert.Empty(Accounts);
    }

    private sealed class FakeMicrosoftAuthProvider : IMicrosoftAuthProvider
    {
        private readonly Result<MicrosoftAuthResult> ResultValue;

        public FakeMicrosoftAuthProvider(Result<MicrosoftAuthResult> ResultValue)
        {
            this.ResultValue = ResultValue;
        }

        public Task<Result<MicrosoftAuthResult>> SignInAsync(CancellationToken CancellationToken = default)
        {
            return Task.FromResult(ResultValue);
        }
    }

    private sealed class FakeAccountRepository : IAccountRepository
    {
        private readonly List<LauncherAccount> Accounts = [];

        public Task<IReadOnlyList<LauncherAccount>> ListAsync(CancellationToken CancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LauncherAccount>>(Accounts.ToList());
        }

        public Task<LauncherAccount?> GetByIdAsync(AccountId AccountId, CancellationToken CancellationToken = default)
        {
            return Task.FromResult(Accounts.FirstOrDefault(x => x.AccountId == AccountId));
        }

        public Task<LauncherAccount?> GetDefaultAsync(CancellationToken CancellationToken = default)
        {
            return Task.FromResult(Accounts.FirstOrDefault(x => x.IsDefault));
        }

        public Task SaveAsync(LauncherAccount Account, CancellationToken CancellationToken = default)
        {
            var ExistingIndex = Accounts.FindIndex(x => x.AccountId == Account.AccountId);
            if (ExistingIndex >= 0)
            {
                Accounts[ExistingIndex] = Account;
            }
            else
            {
                Accounts.Add(Account);
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(AccountId AccountId, CancellationToken CancellationToken = default)
        {
            Accounts.RemoveAll(x => x.AccountId == AccountId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTokenStore : ITokenStore
    {
        private readonly Dictionary<AccountId, string> Tokens = [];

        public Task<Result> SaveRefreshTokenAsync(AccountId AccountId, string RefreshToken, CancellationToken CancellationToken = default)
        {
            Tokens[AccountId] = RefreshToken;
            return Task.FromResult(Result.Success());
        }

        public Task<Result<string>> GetRefreshTokenAsync(AccountId AccountId, CancellationToken CancellationToken = default)
        {
            if (Tokens.TryGetValue(AccountId, out var RefreshToken))
            {
                return Task.FromResult(Result<string>.Success(RefreshToken));
            }

            return Task.FromResult(Result<string>.Failure(AccountErrors.TokenMissing));
        }

        public Task<Result> DeleteRefreshTokenAsync(AccountId AccountId, CancellationToken CancellationToken = default)
        {
            Tokens.Remove(AccountId);
            return Task.FromResult(Result.Success());
        }
    }
}