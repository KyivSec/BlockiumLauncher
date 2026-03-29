using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Persistence.Json;
using BlockiumLauncher.Infrastructure.Persistence.Models;
using BlockiumLauncher.Infrastructure.Persistence.Paths;

namespace BlockiumLauncher.Infrastructure.Persistence.Repositories;

public sealed class JsonJavaInstallationRepository : IJavaInstallationRepository
{
    private readonly ILauncherPaths LauncherPaths;
    private readonly JsonFileStore JsonFileStore;

    public JsonJavaInstallationRepository(
        ILauncherPaths LauncherPaths,
        JsonFileStore JsonFileStore)
    {
        this.LauncherPaths = LauncherPaths;
        this.JsonFileStore = JsonFileStore;
    }

    public async Task<IReadOnlyList<JavaInstallation>> ListAsync(CancellationToken CancellationToken)
    {
        var Items = await ReadAllAsync(CancellationToken);
        return Items.Select(MapToDomain).ToList();
    }

    public async Task<JavaInstallation?> GetByIdAsync(JavaInstallationId JavaInstallationId, CancellationToken CancellationToken)
    {
        var Items = await ReadAllAsync(CancellationToken);
        var Item = Items.FirstOrDefault(Item => string.Equals(Item.JavaInstallationId, JavaInstallationId.ToString(), StringComparison.Ordinal));
        return Item is null ? null : MapToDomain(Item);
    }

    public async Task SaveAsync(JavaInstallation JavaInstallation, CancellationToken CancellationToken)
    {
        var Items = await ReadAllAsync(CancellationToken);
        var Stored = MapFromDomain(JavaInstallation);

        var ExistingIndex = Items.FindIndex(Item => string.Equals(Item.JavaInstallationId, Stored.JavaInstallationId, StringComparison.Ordinal));
        if (ExistingIndex >= 0)
        {
            Items[ExistingIndex] = Stored;
        }
        else
        {
            Items.Add(Stored);
        }

        await JsonFileStore.WriteAsync(LauncherPaths.JavaInstallationsFilePath, Items, CancellationToken);
    }

    public async Task DeleteAsync(JavaInstallationId JavaInstallationId, CancellationToken CancellationToken)
    {
        var Items = await ReadAllAsync(CancellationToken);
        Items.RemoveAll(Item => string.Equals(Item.JavaInstallationId, JavaInstallationId.ToString(), StringComparison.Ordinal));
        await JsonFileStore.WriteAsync(LauncherPaths.JavaInstallationsFilePath, Items, CancellationToken);
    }

    private async Task<List<StoredJavaInstallation>> ReadAllAsync(CancellationToken CancellationToken)
    {
        var Items = await JsonFileStore.ReadOptionalAsync<List<StoredJavaInstallation>>(LauncherPaths.JavaInstallationsFilePath, CancellationToken);
        return Items ?? [];
    }

    private static StoredJavaInstallation MapFromDomain(JavaInstallation JavaInstallation)
    {
        return new StoredJavaInstallation
        {
            JavaInstallationId = JavaInstallation.JavaInstallationId.ToString(),
            ExecutablePath = JavaInstallation.ExecutablePath,
            Version = JavaInstallation.Version,
            Architecture = JavaInstallation.Architecture,
            Vendor = JavaInstallation.Vendor,
            IsValid = JavaInstallation.IsValid
        };
    }

    private static JavaInstallation MapToDomain(StoredJavaInstallation Stored)
    {
        return JavaInstallation.Create(
            new JavaInstallationId(Stored.JavaInstallationId),
            Stored.ExecutablePath,
            Stored.Version,
            Stored.Architecture,
            Stored.Vendor,
            Stored.IsValid);
    }
}
