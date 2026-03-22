using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Security;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Accounts;

public sealed class RemoveAccountUseCase
{
    private readonly IAccountRepository AccountRepository;
    private readonly ITokenStore TokenStore;

    public RemoveAccountUseCase(
        IAccountRepository AccountRepository,
        ITokenStore TokenStore)
    {
        this.AccountRepository = AccountRepository ?? throw new ArgumentNullException(nameof(AccountRepository));
        this.TokenStore = TokenStore ?? throw new ArgumentNullException(nameof(TokenStore));
    }

    public async Task<Result> ExecuteAsync(
        RemoveAccountRequest Request,
        CancellationToken CancellationToken = default)
    {
        if (Request is null)
        {
            return Result.Failure(AccountErrors.InvalidRequest);
        }

        var Accounts = await AccountRepository.ListAsync(CancellationToken).ConfigureAwait(false);
        var Target = Accounts.FirstOrDefault(x => x.AccountId == Request.AccountId);

        if (Target is null)
        {
            return Result.Failure(AccountErrors.AccountNotFound);
        }

        await AccountRepository.DeleteAsync(Request.AccountId, CancellationToken).ConfigureAwait(false);
        await TokenStore.DeleteRefreshTokenAsync(Request.AccountId, CancellationToken).ConfigureAwait(false);

        if (Target.IsDefault)
        {
            var RemainingAccounts = await AccountRepository.ListAsync(CancellationToken).ConfigureAwait(false);
            var Replacement = RemainingAccounts.FirstOrDefault();

            if (Replacement is not null && !Replacement.IsDefault)
            {
                Replacement.SetDefault();
                await AccountRepository.SaveAsync(Replacement, CancellationToken).ConfigureAwait(false);
            }
        }

        return Result.Success();
    }
}