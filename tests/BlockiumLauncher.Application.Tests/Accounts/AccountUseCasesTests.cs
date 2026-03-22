using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Security;
using BlockiumLauncher.Application.UseCases.Accounts;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;
using Xunit;

namespace BlockiumLauncher.Application.Tests.Accounts;

public sealed class AccountUseCasesTests
{
    [Fact]
    public async Task AddAccount_FirstAccount_BecomesDefault()
    {
        var Repository = new FakeAccountRepository();
        var TokenStore = new FakeTokenStore();
        var UseCase = new AddAccountUseCase(Repository, TokenStore);

        var Result = await UseCase.ExecuteAsync(new AddAccountRequest
        {
            Provider = AccountProvider.Microsoft,
            Username = "Steve",
            AccountIdentifier = "xuid-1",
            RefreshToken = "refresh-1",
            SetAsDefault = false
        });

        Assert.True(Result.IsSuccess);
        Assert.True(Result.Value.IsDefault);

        var Accounts = await Repository.ListAsync();
        Assert.Single(Accounts);
        Assert.True(Accounts[0].IsDefault);
    }

    [Fact]
    public async Task AddAccount_SecondAccount_PreservesExistingDefault_WhenNotRequested()
    {
        var Repository = new FakeAccountRepository();
        var TokenStore = new FakeTokenStore();
        var AddUseCase = new AddAccountUseCase(Repository, TokenStore);

        var First = await AddUseCase.ExecuteAsync(new AddAccountRequest
        {
            Provider = AccountProvider.Microsoft,
            Username = "Steve",
            AccountIdentifier = "xuid-1",
            RefreshToken = "refresh-1",
            SetAsDefault = false
        });

        var Second = await AddUseCase.ExecuteAsync(new AddAccountRequest
        {
            Provider = AccountProvider.Microsoft,
            Username = "Alex",
            AccountIdentifier = "xuid-2",
            RefreshToken = "refresh-2",
            SetAsDefault = false
        });

        Assert.True(First.IsSuccess);
        Assert.True(Second.IsSuccess);

        var Accounts = await Repository.ListAsync();
        Assert.Equal(2, Accounts.Count);

        var DefaultAccount = Assert.Single(Accounts, x => x.IsDefault);
        Assert.Equal("Steve", DefaultAccount.Username);
    }

    [Fact]
    public async Task SetDefaultAccount_SwitchesDefaultCorrectly()
    {
        var Repository = new FakeAccountRepository();
        var TokenStore = new FakeTokenStore();
        var AddUseCase = new AddAccountUseCase(Repository, TokenStore);

        var First = await AddUseCase.ExecuteAsync(new AddAccountRequest
        {
            Provider = AccountProvider.Microsoft,
            Username = "Steve",
            AccountIdentifier = "xuid-1",
            RefreshToken = "refresh-1",
            SetAsDefault = false
        });

        var Second = await AddUseCase.ExecuteAsync(new AddAccountRequest
        {
            Provider = AccountProvider.Microsoft,
            Username = "Alex",
            AccountIdentifier = "xuid-2",
            RefreshToken = "refresh-2",
            SetAsDefault = false
        });

        var SetDefaultUseCase = new SetDefaultAccountUseCase(Repository);
        var SetResult = await SetDefaultUseCase.ExecuteAsync(new SetDefaultAccountRequest
        {
            AccountId = Second.Value.AccountId
        });

        Assert.True(SetResult.IsSuccess);

        var Accounts = await Repository.ListAsync();
        var DefaultAccount = Assert.Single(Accounts, x => x.IsDefault);
        Assert.Equal("Alex", DefaultAccount.Username);
    }

    [Fact]
    public async Task RemoveAccount_DefaultAccount_ReassignsDefaultToRemainingAccount()
    {
        var Repository = new FakeAccountRepository();
        var TokenStore = new FakeTokenStore();
        var AddUseCase = new AddAccountUseCase(Repository, TokenStore);

        var First = await AddUseCase.ExecuteAsync(new AddAccountRequest
        {
            Provider = AccountProvider.Microsoft,
            Username = "Steve",
            AccountIdentifier = "xuid-1",
            RefreshToken = "refresh-1",
            SetAsDefault = false
        });

        var Second = await AddUseCase.ExecuteAsync(new AddAccountRequest
        {
            Provider = AccountProvider.Microsoft,
            Username = "Alex",
            AccountIdentifier = "xuid-2",
            RefreshToken = "refresh-2",
            SetAsDefault = false
        });

        var RemoveUseCase = new RemoveAccountUseCase(Repository, TokenStore);
        var RemoveResult = await RemoveUseCase.ExecuteAsync(new RemoveAccountRequest
        {
            AccountId = First.Value.AccountId
        });

        Assert.True(RemoveResult.IsSuccess);

        var Accounts = await Repository.ListAsync();
        Assert.Single(Accounts);
        Assert.True(Accounts[0].IsDefault);
        Assert.Equal("Alex", Accounts[0].Username);
    }

    [Fact]
    public async Task ListAccounts_ReturnsStoredAccounts()
    {
        var Repository = new FakeAccountRepository();
        var TokenStore = new FakeTokenStore();
        var AddUseCase = new AddAccountUseCase(Repository, TokenStore);

        await AddUseCase.ExecuteAsync(new AddAccountRequest
        {
            Provider = AccountProvider.Microsoft,
            Username = "Steve",
            AccountIdentifier = "xuid-1",
            RefreshToken = "refresh-1",
            SetAsDefault = false
        });

        await AddUseCase.ExecuteAsync(new AddAccountRequest
        {
            Provider = AccountProvider.Offline,
            Username = "Builder",
            AccountIdentifier = null,
            RefreshToken = null,
            SetAsDefault = false
        });

        var ListUseCase = new ListAccountsUseCase(Repository);
        var Result = await ListUseCase.ExecuteAsync();

        Assert.True(Result.IsSuccess);
        Assert.Equal(2, Result.Value.Count);
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