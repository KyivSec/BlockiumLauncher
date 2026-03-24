using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Persistence.Json;
using BlockiumLauncher.Infrastructure.Persistence.Models;

namespace BlockiumLauncher.Infrastructure.Persistence.Repositories;

public sealed class JsonAccountRepository : IAccountRepository
{
    private readonly JsonFileStore JsonFileStore;
    private readonly string AccountsFilePath;

    public JsonAccountRepository(JsonFileStore JsonFileStore)
    {
        this.JsonFileStore = JsonFileStore ?? throw new ArgumentNullException(nameof(JsonFileStore));

        AccountsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BlockiumLauncher",
            "accounts.json");
    }

    public async Task<IReadOnlyList<LauncherAccount>> ListAsync(CancellationToken CancellationToken = default)
    {
        var Items = await ReadAllAsync(CancellationToken).ConfigureAwait(false);
        return Items.Select(MapToDomain).ToList();
    }

    public async Task<LauncherAccount?> GetByIdAsync(AccountId AccountId, CancellationToken CancellationToken = default)
    {
        var Items = await ReadAllAsync(CancellationToken).ConfigureAwait(false);
        var Item = Items.FirstOrDefault(Item => string.Equals(Item.AccountId, AccountId.ToString(), StringComparison.Ordinal));
        return Item is null ? null : MapToDomain(Item);
    }

    public async Task<LauncherAccount?> GetDefaultAsync(CancellationToken CancellationToken = default)
    {
        var Items = await ReadAllAsync(CancellationToken).ConfigureAwait(false);
        var Item = Items.FirstOrDefault(Item => Item.IsDefault);
        return Item is null ? null : MapToDomain(Item);
    }

    public async Task SaveAsync(LauncherAccount Account, CancellationToken CancellationToken = default)
    {
        var Items = await ReadAllAsync(CancellationToken).ConfigureAwait(false);
        var Stored = MapFromDomain(Account);

        var ExistingIndex = Items.FindIndex(Item => string.Equals(Item.AccountId, Stored.AccountId, StringComparison.Ordinal));
        if (ExistingIndex >= 0)
        {
            Items[ExistingIndex] = Stored;
        }
        else
        {
            Items.Add(Stored);
        }

        await JsonFileStore.WriteAsync(AccountsFilePath, Items, CancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(AccountId AccountId, CancellationToken CancellationToken = default)
    {
        var Items = await ReadAllAsync(CancellationToken).ConfigureAwait(false);
        Items.RemoveAll(Item => string.Equals(Item.AccountId, AccountId.ToString(), StringComparison.Ordinal));
        await JsonFileStore.WriteAsync(AccountsFilePath, Items, CancellationToken).ConfigureAwait(false);
    }

    private async Task<List<StoredLauncherAccount>> ReadAllAsync(CancellationToken CancellationToken)
    {
        var Items = await JsonFileStore.ReadOptionalAsync<List<StoredLauncherAccount>>(AccountsFilePath, CancellationToken).ConfigureAwait(false);
        return Items ?? [];
    }

    private static StoredLauncherAccount MapFromDomain(LauncherAccount Account)
    {
        return new StoredLauncherAccount
        {
            AccountId = Account.AccountId.ToString(),
            Provider = Account.Provider.ToString(),
            Username = Account.Username,
            AccountIdentifier = Account.AccountIdentifier,
            AccessTokenRef = Account.AccessTokenRef,
            RefreshTokenRef = Account.RefreshTokenRef,
            IsDefault = Account.IsDefault,
            State = Account.State.ToString(),
            ValidatedAtUtc = Account.ValidatedAtUtc
        };
    }

    private static LauncherAccount MapToDomain(StoredLauncherAccount Stored)
    {
        var AccountId = new AccountId(Stored.AccountId);

        if (!Enum.TryParse<AccountProvider>(Stored.Provider, ignoreCase: true, out var Provider))
        {
            throw new InvalidOperationException($"Unknown account provider '{Stored.Provider}'.");
        }

        if (!Enum.TryParse<AccountState>(Stored.State, ignoreCase: true, out var State))
        {
            throw new InvalidOperationException($"Unknown account state '{Stored.State}'.");
        }

        return LauncherAccount.Create(
            AccountId,
            Provider,
            Stored.Username,
            Stored.AccountIdentifier,
            Stored.AccessTokenRef,
            Stored.RefreshTokenRef,
            Stored.IsDefault,
            State,
            Stored.ValidatedAtUtc);
    }
}