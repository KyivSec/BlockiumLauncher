using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Persistence.Json;
using BlockiumLauncher.Infrastructure.Persistence.Models;
using BlockiumLauncher.Infrastructure.Persistence.Paths;

namespace BlockiumLauncher.Infrastructure.Persistence.Repositories;

public sealed class JsonAccountRepository : IAccountRepository
{
    private readonly ILauncherPaths LauncherPaths;
    private readonly JsonFileStore JsonFileStore;

    public JsonAccountRepository(
        ILauncherPaths LauncherPaths,
        JsonFileStore JsonFileStore)
    {
        this.LauncherPaths = LauncherPaths;
        this.JsonFileStore = JsonFileStore;
    }

    public async Task<IReadOnlyList<LauncherAccount>> ListAsync(CancellationToken CancellationToken)
    {
        var Items = await ReadAllAsync(CancellationToken);
        return Items.Select(MapToDomain).ToList();
    }

    public async Task<LauncherAccount?> GetByIdAsync(AccountId AccountId, CancellationToken CancellationToken)
    {
        var Items = await ReadAllAsync(CancellationToken);
        var Item = Items.FirstOrDefault(Item => string.Equals(Item.AccountId, AccountId.ToString(), StringComparison.Ordinal));
        return Item is null ? null : MapToDomain(Item);
    }

    public async Task<LauncherAccount?> GetDefaultAsync(CancellationToken CancellationToken)
    {
        var Items = await ReadAllAsync(CancellationToken);
        var Item = Items.FirstOrDefault(Item => Item.IsDefault);
        return Item is null ? null : MapToDomain(Item);
    }

    public async Task SaveAsync(LauncherAccount Account, CancellationToken CancellationToken)
    {
        var Items = await ReadAllAsync(CancellationToken);
        var Stored = MapFromDomain(Account);

        if (Stored.IsDefault) {
            foreach (var Item in Items) {
                Item.IsDefault = false;
            }
        }

        var ExistingIndex = Items.FindIndex(Item => string.Equals(Item.AccountId, Stored.AccountId, StringComparison.Ordinal));
        if (ExistingIndex >= 0) {
            Items[ExistingIndex] = Stored;
        }
        else {
            Items.Add(Stored);
        }

        await JsonFileStore.WriteAsync(LauncherPaths.AccountsFilePath, Items, CancellationToken);
    }

    public async Task DeleteAsync(AccountId AccountId, CancellationToken CancellationToken)
    {
        var Items = await ReadAllAsync(CancellationToken);
        Items.RemoveAll(Item => string.Equals(Item.AccountId, AccountId.ToString(), StringComparison.Ordinal));
        await JsonFileStore.WriteAsync(LauncherPaths.AccountsFilePath, Items, CancellationToken);
    }

    private async Task<List<StoredLauncherAccount>> ReadAllAsync(CancellationToken CancellationToken)
    {
        var Items = await JsonFileStore.ReadOptionalAsync<List<StoredLauncherAccount>>(LauncherPaths.AccountsFilePath, CancellationToken);
        return Items ?? [];
    }

    private static StoredLauncherAccount MapFromDomain(LauncherAccount Account)
    {
        throw new NotImplementedException("Wire this mapper to your real Stage 3 LauncherAccount API.");
    }

    private static LauncherAccount MapToDomain(StoredLauncherAccount Stored)
    {
        throw new NotImplementedException("Wire this mapper to your real Stage 3 LauncherAccount API.");
    }
}
