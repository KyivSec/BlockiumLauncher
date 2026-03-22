namespace BlockiumLauncher.Infrastructure.Persistence.Models;

public sealed class StoredLauncherAccount
{
    public string AccountId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? AccountIdentifier { get; set; }
    public string? AccessTokenRef { get; set; }
    public string? RefreshTokenRef { get; set; }
    public bool IsDefault { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? ValidatedAtUtc { get; set; }
}