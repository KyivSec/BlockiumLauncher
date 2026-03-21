using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Accounts;

public sealed class RefreshAccountRequest
{
    public AccountId AccountId { get; }

    public RefreshAccountRequest(AccountId AccountId)
    {
        this.AccountId = AccountId;
    }
}
