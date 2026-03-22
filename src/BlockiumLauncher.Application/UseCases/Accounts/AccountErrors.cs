using BlockiumLauncher.Shared.Errors;

namespace BlockiumLauncher.Application.UseCases.Accounts;

public static class AccountErrors
{
    public static readonly Error InvalidRequest = new("Account.InvalidRequest", "The account request is invalid.");
    public static readonly Error AccountNotFound = new("Account.NotFound", "The requested account was not found.");
    public static readonly Error TokenMissing = new("Account.TokenMissing", "The requested account token was not found.");
    public static readonly Error PersistenceFailed = new("Account.PersistenceFailed", "Failed to persist account data.");
    public static readonly Error NoDefaultAccount = new("Account.NoDefaultAccount", "No default account is configured.");
    public static readonly Error NoOfflineAccount = new("Account.NoOfflineAccount", "No offline account is available.");
    public static readonly Error AccountNotUsableForOfflineLaunch = new("Account.AccountNotUsableForOfflineLaunch", "The selected account cannot be used for offline launch.");
}