using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Accounts;

public sealed class SetDefaultAccountRequest
{
    public AccountId AccountId { get; init; }
}