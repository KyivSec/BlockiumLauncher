using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.UseCases.Accounts;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using Xunit;

namespace BlockiumLauncher.Application.Tests.Accounts;

public sealed class ResolveOfflineLaunchAccountUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ResolvesRequestedOfflineAccount()
    {
        var Account = LauncherAccount.CreateOffline(new AccountId("offline-1"), "Builder", null, true);
        var Repository = new FakeAccountRepository(Account);
        var UseCase = new ResolveOfflineLaunchAccountUseCase(Repository);

        var Result = await UseCase.ExecuteAsync(new ResolveOfflineLaunchAccountRequest
        {
            AccountId = Account.AccountId
        });

        Assert.True(Result.IsSuccess);
        Assert.Equal("Builder", Result.Value.Username);
        Assert.True(Result.Value.IsOffline);
        Assert.Equal(Account.AccountId.ToString(), Result.Value.AccountId);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_ForRequestedMicrosoftAccount()
    {
        var Account = LauncherAccount.CreateMicrosoft(new AccountId("msa-1"), "Steve", "xuid-1", "refresh-ref", true);
        var Repository = new FakeAccountRepository(Account);
        var UseCase = new ResolveOfflineLaunchAccountUseCase(Repository);

        var Result = await UseCase.ExecuteAsync(new ResolveOfflineLaunchAccountRequest
        {
            AccountId = Account.AccountId
        });

        Assert.True(Result.IsFailure);
        Assert.Equal(AccountErrors.AccountNotUsableForOfflineLaunch.Code, Result.Error.Code);
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesDefaultOfflineAccount()
    {
        var Account = LauncherAccount.CreateOffline(new AccountId("offline-1"), "Builder", null, true);
        var Repository = new FakeAccountRepository(Account);
        var UseCase = new ResolveOfflineLaunchAccountUseCase(Repository);

        var Result = await UseCase.ExecuteAsync();

        Assert.True(Result.IsSuccess);
        Assert.Equal("Builder", Result.Value.Username);
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesSingleOfflineAccount_WhenNoDefaultExists()
    {
        var Account = LauncherAccount.CreateOffline(new AccountId("offline-1"), "Solo");
        var Repository = new FakeAccountRepository(Account);
        var UseCase = new ResolveOfflineLaunchAccountUseCase(Repository);

        var Result = await UseCase.ExecuteAsync();

        Assert.True(Result.IsSuccess);
        Assert.Equal("Solo", Result.Value.Username);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenNoOfflineAccountsExist()
    {
        var Account = LauncherAccount.CreateMicrosoft(new AccountId("msa-1"), "Steve", "xuid-1", "refresh-ref", true);
        var Repository = new FakeAccountRepository(Account);
        var UseCase = new ResolveOfflineLaunchAccountUseCase(Repository);

        var Result = await UseCase.ExecuteAsync();

        Assert.True(Result.IsFailure);
        Assert.Equal(AccountErrors.AccountNotUsableForOfflineLaunch.Code, Result.Error.Code);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_ForRemovedOfflineAccount()
    {
        var Account = LauncherAccount.CreateOffline(new AccountId("offline-1"), "Ghost", null, true);
        Account.MarkRemoved();

        var Repository = new FakeAccountRepository(Account);
        var UseCase = new ResolveOfflineLaunchAccountUseCase(Repository);

        var Result = await UseCase.ExecuteAsync(new ResolveOfflineLaunchAccountRequest
        {
            AccountId = Account.AccountId
        });

        Assert.True(Result.IsFailure);
        Assert.Equal(AccountErrors.AccountNotUsableForOfflineLaunch.Code, Result.Error.Code);
    }

    [Fact]
    public void CreateOfflinePlayerUuid_IsDeterministic()
    {
        var First = ResolveOfflineLaunchAccountUseCase.CreateOfflinePlayerUuid("Builder");
        var Second = ResolveOfflineLaunchAccountUseCase.CreateOfflinePlayerUuid("Builder");
        var Third = ResolveOfflineLaunchAccountUseCase.CreateOfflinePlayerUuid("BuilderTwo");

        Assert.Equal(First, Second);
        Assert.NotEqual(First, Third);
    }

    [Fact]
    public async Task GetDefaultAccountUseCase_ReturnsDefaultAccount()
    {
        var Account = LauncherAccount.CreateOffline(new AccountId("offline-1"), "Builder", null, true);
        var Repository = new FakeAccountRepository(Account);
        var UseCase = new GetDefaultAccountUseCase(Repository);

        var Result = await UseCase.ExecuteAsync();

        Assert.True(Result.IsSuccess);
        Assert.Equal("Builder", Result.Value.Username);
    }

    [Fact]
    public async Task GetDefaultAccountUseCase_ReturnsFailure_WhenNoDefaultExists()
    {
        var Repository = new FakeAccountRepository();
        var UseCase = new GetDefaultAccountUseCase(Repository);

        var Result = await UseCase.ExecuteAsync();

        Assert.True(Result.IsFailure);
        Assert.Equal(AccountErrors.NoDefaultAccount.Code, Result.Error.Code);
    }

    private sealed class FakeAccountRepository : IAccountRepository
    {
        private readonly List<LauncherAccount> Accounts = [];

        public FakeAccountRepository(params LauncherAccount[] Accounts)
        {
            this.Accounts.AddRange(Accounts);
        }

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
}