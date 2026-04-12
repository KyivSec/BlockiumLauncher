namespace BlockiumLauncher.Contracts.Accounts
{
    public sealed class LaunchAccountContextDto
    {
        public string AccountId { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;
        public string PlayerUuid { get; init; } = string.Empty;
        public bool IsOffline { get; init; }
        public string UserPropertiesJson { get; init; } = "{}";
    }
}
