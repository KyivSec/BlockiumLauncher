using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Persistence.Json;
using BlockiumLauncher.Infrastructure.Persistence.Models;
using BlockiumLauncher.Infrastructure.Persistence.Paths;

namespace BlockiumLauncher.Infrastructure.Persistence.Repositories;

public sealed class JsonInstanceRepository : IInstanceRepository
{
    private readonly ILauncherPaths LauncherPaths;
    private readonly JsonFileStore JsonFileStore;

    public JsonInstanceRepository(
        ILauncherPaths LauncherPaths,
        JsonFileStore JsonFileStore)
    {
        this.LauncherPaths = LauncherPaths;
        this.JsonFileStore = JsonFileStore;
    }

    public async Task<IReadOnlyList<LauncherInstance>> ListAsync(CancellationToken CancellationToken)
    {
        var Items = await ReadAllAsync(CancellationToken);
        return Items.Select(MapToDomain).ToList();
    }

    public async Task<LauncherInstance?> GetByIdAsync(InstanceId InstanceId, CancellationToken CancellationToken)
    {
        var Items = await ReadAllAsync(CancellationToken);
        var Item = Items.FirstOrDefault(Item => string.Equals(Item.InstanceId, InstanceId.ToString(), StringComparison.Ordinal));
        return Item is null ? null : MapToDomain(Item);
    }

    public async Task<LauncherInstance?> GetByNameAsync(string Name, CancellationToken CancellationToken)
    {
        var NormalizedName = Name.Trim();
        var Items = await ReadAllAsync(CancellationToken);
        var Item = Items.FirstOrDefault(Item => string.Equals(Item.Name, NormalizedName, StringComparison.OrdinalIgnoreCase));
        return Item is null ? null : MapToDomain(Item);
    }

    public async Task SaveAsync(LauncherInstance Instance, CancellationToken CancellationToken)
    {
        var Items = await ReadAllAsync(CancellationToken);
        var Stored = MapFromDomain(Instance);

        var ExistingIndex = Items.FindIndex(Item => string.Equals(Item.InstanceId, Stored.InstanceId, StringComparison.Ordinal));
        if (ExistingIndex >= 0) {
            Items[ExistingIndex] = Stored;
        }
        else {
            Items.Add(Stored);
        }

        await JsonFileStore.WriteAsync(LauncherPaths.InstancesFilePath, Items, CancellationToken);
    }

    public async Task DeleteAsync(InstanceId InstanceId, CancellationToken CancellationToken)
    {
        var Items = await ReadAllAsync(CancellationToken);
        Items.RemoveAll(Item => string.Equals(Item.InstanceId, InstanceId.ToString(), StringComparison.Ordinal));
        await JsonFileStore.WriteAsync(LauncherPaths.InstancesFilePath, Items, CancellationToken);
    }

    private async Task<List<StoredLauncherInstance>> ReadAllAsync(CancellationToken CancellationToken)
    {
        var Items = await JsonFileStore.ReadOptionalAsync<List<StoredLauncherInstance>>(LauncherPaths.InstancesFilePath, CancellationToken);
        return Items ?? [];
    }

    private static StoredLauncherInstance MapFromDomain(LauncherInstance Instance)
    {
        throw new NotImplementedException("Wire this mapper to your real Stage 3 LauncherInstance API.");
    }

    private static LauncherInstance MapToDomain(StoredLauncherInstance Stored)
    {
        throw new NotImplementedException("Wire this mapper to your real Stage 3 LauncherInstance API.");
    }
}
