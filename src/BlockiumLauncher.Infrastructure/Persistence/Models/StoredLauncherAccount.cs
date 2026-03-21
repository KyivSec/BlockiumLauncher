using BlockiumLauncher.Domain.Enums;

namespace BlockiumLauncher.Infrastructure.Persistence.Models;

public sealed class StoredLauncherAccount
{
    public string AccountId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public AccountProvider Provider { get; set; }
    public string AccessTokenRef { get; set; } = string.Empty;
    public string? RefreshTokenRef { get; set; }
    public bool IsDefault { get; set; }
    public DateTimeOffset? LastValidatedAtUtc { get; set; }
    public AccountState State { get; set; }
}
