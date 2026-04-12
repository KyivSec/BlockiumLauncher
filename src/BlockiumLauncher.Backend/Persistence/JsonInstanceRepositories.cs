using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Persistence.Json;
using BlockiumLauncher.Infrastructure.Persistence.Models;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using AppLauncherPaths = BlockiumLauncher.Application.Abstractions.Paths.ILauncherPaths;

namespace BlockiumLauncher.Infrastructure.Persistence.Repositories;

public sealed class JsonInstanceContentMetadataRepository : IInstanceContentMetadataRepository
{
    private readonly AppLauncherPaths launcherPaths;
    private readonly JsonFileStore jsonFileStore;

    public JsonInstanceContentMetadataRepository(
        AppLauncherPaths launcherPaths,
        JsonFileStore jsonFileStore)
    {
        this.launcherPaths = launcherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
        this.jsonFileStore = jsonFileStore ?? throw new ArgumentNullException(nameof(jsonFileStore));
    }

    public Task<InstanceContentMetadata?> LoadAsync(string installLocation, CancellationToken cancellationToken = default)
    {
        return jsonFileStore.ReadOptionalAsync<InstanceContentMetadata>(
            launcherPaths.GetInstanceMetadataFilePath(installLocation),
            cancellationToken);
    }

    public Task SaveAsync(string installLocation, InstanceContentMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return jsonFileStore.WriteAsync(
            launcherPaths.GetInstanceMetadataFilePath(installLocation),
            metadata,
            cancellationToken);
    }
}

public sealed class JsonInstanceModpackMetadataRepository : IInstanceModpackMetadataRepository
{
    private readonly AppLauncherPaths launcherPaths;
    private readonly JsonFileStore jsonFileStore;

    public JsonInstanceModpackMetadataRepository(
        AppLauncherPaths launcherPaths,
        JsonFileStore jsonFileStore)
    {
        this.launcherPaths = launcherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
        this.jsonFileStore = jsonFileStore ?? throw new ArgumentNullException(nameof(jsonFileStore));
    }

    public Task<InstanceModpackMetadata?> LoadAsync(string installLocation, CancellationToken cancellationToken = default)
    {
        return jsonFileStore.ReadOptionalAsync<InstanceModpackMetadata>(
            launcherPaths.GetInstanceModpackMetadataFilePath(installLocation),
            cancellationToken);
    }

    public Task SaveAsync(string installLocation, InstanceModpackMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return jsonFileStore.WriteAsync(
            launcherPaths.GetInstanceModpackMetadataFilePath(installLocation),
            metadata,
            cancellationToken);
    }

    public Task DeleteAsync(string installLocation, CancellationToken cancellationToken = default)
    {
        var path = launcherPaths.GetInstanceModpackMetadataFilePath(installLocation);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }
}

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
        if (ExistingIndex >= 0)
        {
            Items[ExistingIndex] = Stored;
        }
        else
        {
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
        return new StoredLauncherInstance
        {
            InstanceId = Instance.InstanceId.ToString(),
            Name = Instance.Name,
            GameVersion = Instance.GameVersion.ToString(),
            LoaderType = Instance.LoaderType,
            LoaderVersion = Instance.LoaderVersion?.ToString(),
            State = Instance.State,
            InstallLocation = Instance.InstallLocation,
            CreatedAtUtc = Instance.CreatedAtUtc,
            LastPlayedAtUtc = Instance.LastPlayedAtUtc,
            IconKey = Instance.IconKey,
            LaunchProfile = new StoredLaunchProfile
            {
                MinMemoryMb = Instance.LaunchProfile.MinMemoryMb,
                MaxMemoryMb = Instance.LaunchProfile.MaxMemoryMb,
                PreferredJavaMajor = Instance.LaunchProfile.PreferredJavaMajor,
                SkipCompatibilityChecks = Instance.LaunchProfile.SkipCompatibilityChecks,
                ExtraJvmArgs = Instance.LaunchProfile.ExtraJvmArgs.ToList(),
                ExtraGameArgs = Instance.LaunchProfile.ExtraGameArgs.ToList(),
                EnvironmentVariables = new Dictionary<string, string>(Instance.LaunchProfile.EnvironmentVariables, StringComparer.Ordinal)
            }
        };
    }

    private static LauncherInstance MapToDomain(StoredLauncherInstance Stored)
    {
        var Instance = LauncherInstance.Create(
            new InstanceId(Stored.InstanceId),
            Stored.Name,
            CreateVersionId(Stored.GameVersion),
            Stored.LoaderType,
            string.IsNullOrWhiteSpace(Stored.LoaderVersion) ? null : CreateVersionId(Stored.LoaderVersion),
            Stored.InstallLocation,
            Stored.CreatedAtUtc,
            new LaunchProfile(
                Stored.LaunchProfile.MinMemoryMb,
                Stored.LaunchProfile.MaxMemoryMb,
                Stored.LaunchProfile.ExtraJvmArgs ?? [],
                Stored.LaunchProfile.ExtraGameArgs ?? [],
                Stored.LaunchProfile.EnvironmentVariables ?? new Dictionary<string, string>(StringComparer.Ordinal),
                Stored.LaunchProfile.PreferredJavaMajor,
                Stored.LaunchProfile.SkipCompatibilityChecks),
            Stored.IconKey);

        switch (Stored.State)
        {
            case InstanceState.Installed:
                Instance.MarkInstalled();
                break;
            case InstanceState.NeedsRepair:
                Instance.MarkNeedsRepair();
                break;
            case InstanceState.Updating:
                Instance.MarkUpdating();
                break;
            case InstanceState.Broken:
                Instance.MarkBroken();
                break;
            case InstanceState.Deleted:
                Instance.MarkDeleted();
                break;
        }

        if (Stored.LastPlayedAtUtc is not null && Instance.State == InstanceState.Installed)
        {
            Instance.RecordLaunch(Stored.LastPlayedAtUtc.Value);
        }

        return Instance;
    }

    private static VersionId CreateVersionId(string Value)
    {
        return VersionId.Parse(Value);
    }
}
