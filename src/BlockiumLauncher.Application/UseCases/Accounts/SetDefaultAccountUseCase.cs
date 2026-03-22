using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Accounts;

public sealed class SetDefaultAccountUseCase
{
    private readonly IAccountRepository AccountRepository;

    public SetDefaultAccountUseCase(IAccountRepository AccountRepository)
    {
        this.AccountRepository = AccountRepository ?? throw new ArgumentNullException(nameof(AccountRepository));
    }

    public async Task<Result> ExecuteAsync(
        SetDefaultAccountRequest Request,
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

        foreach (var Account in Accounts)
        {
            if (Account.AccountId == Request.AccountId)
            {
                if (!Account.IsDefault)
                {
                    Account.SetDefault();
                    await AccountRepository.SaveAsync(Account, CancellationToken).ConfigureAwait(false);
                }
            }
            else if (Account.IsDefault)
            {
                Account.ClearDefault();
                await AccountRepository.SaveAsync(Account, CancellationToken).ConfigureAwait(false);
            }
        }

        return Result.Success();
    }
}