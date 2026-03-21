using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Accounts;

public sealed class RemoveAccountRequest
{
    public AccountId AccountId { get; }

    public RemoveAccountRequest(AccountId AccountId)
    {
        this.AccountId = AccountId;
    }
}
