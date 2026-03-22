using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Security;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Accounts;

public sealed class AddAccountUseCase
{
    private readonly IAccountRepository AccountRepository;
    private readonly ITokenStore TokenStore;

    public AddAccountUseCase(
        IAccountRepository AccountRepository,
        ITokenStore TokenStore)
    {
        this.AccountRepository = AccountRepository ?? throw new ArgumentNullException(nameof(AccountRepository));
        this.TokenStore = TokenStore ?? throw new ArgumentNullException(nameof(TokenStore));
    }

    public async Task<Result<LauncherAccount>> ExecuteAsync(
        AddAccountRequest Request,
        CancellationToken CancellationToken = default)
    {
        if (Request is null || string.IsNullOrWhiteSpace(Request.Username))
        {
            return Result<LauncherAccount>.Failure(AccountErrors.InvalidRequest);
        }

        var ExistingAccounts = await AccountRepository.ListAsync(CancellationToken).ConfigureAwait(false);
        var ShouldBeDefault = Request.SetAsDefault || ExistingAccounts.Count == 0;
        var NewAccountId = AccountId.New();

        if (ShouldBeDefault)
        {
            foreach (var ExistingAccount in ExistingAccounts)
            {
                if (ExistingAccount.IsDefault)
                {
                    ExistingAccount.ClearDefault();
                    await AccountRepository.SaveAsync(ExistingAccount, CancellationToken).ConfigureAwait(false);
                }
            }
        }

        var RefreshTokenRef = string.IsNullOrWhiteSpace(Request.RefreshToken)
            ? null
            : "account-refresh:" + NewAccountId;

        LauncherAccount Account;
        if (Request.Provider == BlockiumLauncher.Domain.Enums.AccountProvider.Microsoft)
        {
            if (string.IsNullOrWhiteSpace(Request.AccountIdentifier))
            {
                return Result<LauncherAccount>.Failure(AccountErrors.InvalidRequest);
            }

            Account = LauncherAccount.CreateMicrosoft(
                NewAccountId,
                Request.Username,
                Request.AccountIdentifier,
                RefreshTokenRef,
                ShouldBeDefault);
        }
        else
        {
            Account = LauncherAccount.CreateOffline(
                NewAccountId,
                Request.Username,
                null,
                ShouldBeDefault);
        }

        await AccountRepository.SaveAsync(Account, CancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(Request.RefreshToken))
        {
            var TokenResult = await TokenStore.SaveRefreshTokenAsync(
                Account.AccountId,
                Request.RefreshToken,
                CancellationToken).ConfigureAwait(false);

            if (TokenResult.IsFailure)
            {
                return Result<LauncherAccount>.Failure(TokenResult.Error);
            }
        }

        return Result<LauncherAccount>.Success(Account);
    }
}