using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Domain.Entities;

public sealed class LauncherAccount
{
    public AccountId AccountId { get; }
    public string DisplayName { get; private set; }
    public AccountProvider Provider { get; }
    public string AccessTokenRef { get; private set; }
    public string? RefreshTokenRef { get; private set; }
    public bool IsDefault { get; private set; }
    public DateTimeOffset? LastValidatedAtUtc { get; private set; }
    public AccountState State { get; private set; }

    private LauncherAccount(
        AccountId AccountId,
        string DisplayName,
        AccountProvider Provider,
        string AccessTokenRef,
        string? RefreshTokenRef,
        bool IsDefault)
    {
        this.AccountId = AccountId;
        this.DisplayName = NormalizeRequired(DisplayName, nameof(DisplayName));
        this.Provider = Provider;
        this.AccessTokenRef = NormalizeRequired(AccessTokenRef, nameof(AccessTokenRef));
        this.RefreshTokenRef = NormalizeOptional(RefreshTokenRef);
        this.IsDefault = IsDefault;
        State = AccountState.Active;

        ValidateProvider(Provider, this.RefreshTokenRef);
    }

    public static LauncherAccount CreateOffline(
        AccountId AccountId,
        string DisplayName,
        string AccessTokenRef,
        bool IsDefault = false)
    {
        return new(
            AccountId,
            DisplayName,
            AccountProvider.Offline,
            AccessTokenRef,
            null,
            IsDefault);
    }

    public static LauncherAccount CreateMicrosoft(
        AccountId AccountId,
        string DisplayName,
        string AccessTokenRef,
        string RefreshTokenRef,
        bool IsDefault = false)
    {
        return new(
            AccountId,
            DisplayName,
            AccountProvider.Microsoft,
            AccessTokenRef,
            RefreshTokenRef,
            IsDefault);
    }

    public void Rename(string DisplayName)
    {
        EnsureNotRemoved();
        this.DisplayName = NormalizeRequired(DisplayName, nameof(DisplayName));
    }

    public void MarkDefault()
    {
        EnsureNotRemoved();
        IsDefault = true;
    }

    public void ClearDefault()
    {
        IsDefault = false;
    }

    public void MarkValidated(DateTimeOffset TimestampUtc)
    {
        EnsureNotRemoved();
        State = AccountState.Active;
        LastValidatedAtUtc = TimestampUtc;
    }

    public void MarkExpired()
    {
        EnsureNotRemoved();
        State = AccountState.Expired;
    }

    public void MarkInvalid()
    {
        EnsureNotRemoved();
        State = AccountState.Invalid;
    }

    public void MarkRemoved()
    {
        State = AccountState.Removed;
        IsDefault = false;
    }

    private void EnsureNotRemoved()
    {
        if (State == AccountState.Removed)
        {
            throw new InvalidOperationException("Removed accounts cannot be modified.");
        }
    }

    private static void ValidateProvider(AccountProvider Provider, string? RefreshTokenRef)
    {
        if (Provider == AccountProvider.Microsoft && string.IsNullOrWhiteSpace(RefreshTokenRef))
        {
            throw new ArgumentException("Microsoft accounts require a refresh token reference.", nameof(RefreshTokenRef));
        }
    }

    private static string NormalizeRequired(string Value, string ParameterName)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", ParameterName);
        }

        return Value.Trim();
    }

    private static string? NormalizeOptional(string? Value)
    {
        return string.IsNullOrWhiteSpace(Value) ? null : Value.Trim();
    }
}
