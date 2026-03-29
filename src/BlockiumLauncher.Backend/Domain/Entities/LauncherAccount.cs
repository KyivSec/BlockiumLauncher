using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Domain.Entities;

public sealed class LauncherAccount
{
    public AccountId AccountId { get; }
    public AccountProvider Provider { get; }
    public string Username { get; private set; }
    public string? AccountIdentifier { get; private set; }
    public string? AccessTokenRef { get; private set; }
    public string? RefreshTokenRef { get; private set; }
    public bool IsDefault { get; private set; }
    public AccountState State { get; private set; }
    public DateTimeOffset? ValidatedAtUtc { get; private set; }

    private LauncherAccount(
        AccountId AccountId,
        AccountProvider Provider,
        string Username,
        string? AccountIdentifier,
        string? AccessTokenRef,
        string? RefreshTokenRef,
        bool IsDefault,
        AccountState State,
        DateTimeOffset? ValidatedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            throw new ArgumentException("Username cannot be null or whitespace.", nameof(Username));
        }

        if (Provider == AccountProvider.Microsoft && string.IsNullOrWhiteSpace(AccountIdentifier))
        {
            throw new ArgumentException("Microsoft accounts require an account identifier.", nameof(AccountIdentifier));
        }

        if (Provider == AccountProvider.Microsoft && string.IsNullOrWhiteSpace(RefreshTokenRef))
        {
            throw new ArgumentException("Microsoft accounts require a refresh token reference.", nameof(RefreshTokenRef));
        }

        if (Provider == AccountProvider.Offline && !string.IsNullOrWhiteSpace(AccountIdentifier))
        {
            throw new ArgumentException("Offline accounts cannot define an account identifier.", nameof(AccountIdentifier));
        }

        this.AccountId = AccountId;
        this.Provider = Provider;
        this.Username = Username.Trim();
        this.AccountIdentifier = Normalize(AccountIdentifier);
        this.AccessTokenRef = Normalize(AccessTokenRef);
        this.RefreshTokenRef = Normalize(RefreshTokenRef);
        this.IsDefault = IsDefault;
        this.State = State;
        this.ValidatedAtUtc = ValidatedAtUtc;
    }

    public static LauncherAccount Create(
        AccountId AccountId,
        AccountProvider Provider,
        string Username,
        string? AccountIdentifier,
        string? AccessTokenRef,
        string? RefreshTokenRef,
        bool IsDefault,
        AccountState State = AccountState.Invalid,
        DateTimeOffset? ValidatedAtUtc = null)
    {
        return new LauncherAccount(
            AccountId,
            Provider,
            Username,
            AccountIdentifier,
            AccessTokenRef,
            RefreshTokenRef,
            IsDefault,
            State,
            ValidatedAtUtc);
    }

    public static LauncherAccount CreateOffline(
        AccountId AccountId,
        string Username,
        string? AccessTokenRef = null,
        bool IsDefault = false)
    {
        return new LauncherAccount(
            AccountId,
            AccountProvider.Offline,
            Username,
            null,
            AccessTokenRef,
            null,
            IsDefault,
            AccountState.Invalid,
            null);
    }

    public static LauncherAccount CreateOffline(
        string Username,
        string? AccessTokenRef = null,
        bool IsDefault = false)
    {
        return CreateOffline(AccountId.New(), Username, AccessTokenRef, IsDefault);
    }

    public static LauncherAccount CreateMicrosoft(
        AccountId AccountId,
        string Username,
        string AccountIdentifier,
        string? RefreshTokenRef,
        bool IsDefault = false)
    {
        return new LauncherAccount(
            AccountId,
            AccountProvider.Microsoft,
            Username,
            AccountIdentifier,
            null,
            RefreshTokenRef,
            IsDefault,
            AccountState.Invalid,
            null);
    }

    public static LauncherAccount CreateMicrosoft(
        string Username,
        string AccountIdentifier,
        string? RefreshTokenRef,
        bool IsDefault = false)
    {
        return CreateMicrosoft(AccountId.New(), Username, AccountIdentifier, RefreshTokenRef, IsDefault);
    }

    public void Rename(string Username)
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            throw new ArgumentException("Username cannot be null or whitespace.", nameof(Username));
        }

        this.Username = Username.Trim();
    }

    public void SetAccountIdentifier(string? AccountIdentifier)
    {
        if (Provider == AccountProvider.Microsoft && string.IsNullOrWhiteSpace(AccountIdentifier))
        {
            throw new ArgumentException("Microsoft accounts require an account identifier.", nameof(AccountIdentifier));
        }

        if (Provider == AccountProvider.Offline && !string.IsNullOrWhiteSpace(AccountIdentifier))
        {
            throw new InvalidOperationException("Offline accounts cannot define an account identifier.");
        }

        this.AccountIdentifier = Normalize(AccountIdentifier);
    }

    public void SetAccessTokenRef(string? AccessTokenRef)
    {
        this.AccessTokenRef = Normalize(AccessTokenRef);
    }

    public void SetRefreshTokenRef(string? RefreshTokenRef)
    {
        if (Provider == AccountProvider.Microsoft && string.IsNullOrWhiteSpace(RefreshTokenRef))
        {
            throw new ArgumentException("Microsoft accounts require a refresh token reference.", nameof(RefreshTokenRef));
        }

        this.RefreshTokenRef = Normalize(RefreshTokenRef);
    }

    public void MarkValidated(DateTimeOffset ValidatedAtUtc)
    {
        if (State == AccountState.Removed)
        {
            throw new InvalidOperationException("Removed accounts cannot be validated.");
        }

        State = AccountState.Active;
        this.ValidatedAtUtc = ValidatedAtUtc;
    }

    public void MarkRemoved()
    {
        State = AccountState.Removed;
        IsDefault = false;
    }

    public void SetDefault()
    {
        if (State == AccountState.Removed)
        {
            throw new InvalidOperationException("Removed accounts cannot be default.");
        }

        IsDefault = true;
    }

    public void ClearDefault()
    {
        IsDefault = false;
    }

    private static string? Normalize(string? Value)
    {
        return string.IsNullOrWhiteSpace(Value) ? null : Value.Trim();
    }
}