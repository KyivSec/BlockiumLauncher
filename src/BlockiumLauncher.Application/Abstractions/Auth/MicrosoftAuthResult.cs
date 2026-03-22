namespace BlockiumLauncher.Application.Abstractions.Auth;

public sealed class MicrosoftAuthResult
{
    public string Username { get; init; } = string.Empty;
    public string AccountIdentifier { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
}