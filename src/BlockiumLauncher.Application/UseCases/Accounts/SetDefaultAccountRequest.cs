using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Accounts;

public sealed class SetDefaultAccountRequest
{
    public AccountId AccountId { get; }

    public SetDefaultAccountRequest(AccountId AccountId)
    {
        this.AccountId = AccountId;
    }
}
