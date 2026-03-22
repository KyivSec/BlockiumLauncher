using BlockiumLauncher.Domain.Enums;

namespace BlockiumLauncher.Application.UseCases.Accounts;

public sealed class AddAccountRequest
{
    public AccountProvider Provider { get; init; }
    public string Username { get; init; } = string.Empty;
    public string? AccountIdentifier { get; init; }
    public string? RefreshToken { get; init; }
    public bool SetAsDefault { get; init; }
}