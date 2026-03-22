using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Accounts;

public sealed class ResolveOfflineLaunchAccountRequest
{
    public AccountId? AccountId { get; init; }
}