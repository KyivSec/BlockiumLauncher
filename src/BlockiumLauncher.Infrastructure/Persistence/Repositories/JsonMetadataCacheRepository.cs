using System.Text.Json;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Persistence.Json;
using BlockiumLauncher.Infrastructure.Persistence.Models;
using BlockiumLauncher.Infrastructure.Persistence.Paths;

namespace BlockiumLauncher.Infrastructure.Persistence.Repositories;

public sealed class JsonMetadataCacheRepository : IMetadataCacheRepository
{
    private readonly ILauncherPaths LauncherPaths;
    private readonly JsonFileStore JsonFileStore;

    public JsonMetadataCacheRepository(
        ILauncherPaths LauncherPaths,
        JsonFileStore JsonFileStore)
    {
        this.LauncherPaths = LauncherPaths;
        this.JsonFileStore = JsonFileStore;
    }

    public async Task<IReadOnlyList<VersionSummary>?> GetCachedVersionsAsync(CancellationToken CancellationToken)
    {
        try
        {
            var Cache = await JsonFileStore.ReadOptionalAsync<StoredVersionCache>(
                LauncherPaths.VersionsCacheFilePath,
                CancellationToken);

            if (Cache is null)
            {
                return null;
            }

            return Cache.Versions.Select(MapToVersionSummary).ToList();
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    public Task SaveCachedVersionsAsync(IReadOnlyList<VersionSummary> Versions, CancellationToken CancellationToken)
    {
        var Cache = new StoredVersionCache
        {
            Versions = Versions.Select(MapFromVersionSummary).ToList()
        };

        return JsonFileStore.WriteAsync(
            LauncherPaths.VersionsCacheFilePath,
            Cache,
            CancellationToken);
    }

    public async Task<IReadOnlyList<LoaderVersionSummary>?> GetCachedLoaderVersionsAsync(
        LoaderType LoaderType,
        VersionId GameVersion,
        CancellationToken CancellationToken)
    {
        var FilePath = LauncherPaths.GetLoaderVersionsCacheFilePath(LoaderType, GameVersion);

        try
        {
            var Cache = await JsonFileStore.ReadOptionalAsync<StoredLoaderVersionCache>(FilePath, CancellationToken);

            if (Cache is null)
            {
                return null;
            }

            return Cache.LoaderVersions.Select(MapToLoaderVersionSummary).ToList();
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    public Task SaveCachedLoaderVersionsAsync(
        LoaderType LoaderType,
        VersionId GameVersion,
        IReadOnlyList<LoaderVersionSummary> LoaderVersions,
        CancellationToken CancellationToken)
    {
        var Cache = new StoredLoaderVersionCache
        {
            LoaderVersions = LoaderVersions.Select(MapFromLoaderVersionSummary).ToList()
        };

        return JsonFileStore.WriteAsync(
            LauncherPaths.GetLoaderVersionsCacheFilePath(LoaderType, GameVersion),
            Cache,
            CancellationToken);
    }

    private static StoredVersionSummary MapFromVersionSummary(VersionSummary Summary)
    {
        return new StoredVersionSummary
        {
            VersionId = Summary.VersionId.ToString() ?? string.Empty,
            DisplayName = Summary.DisplayName,
            IsRelease = Summary.IsRelease,
            ReleasedAtUtc = Summary.ReleasedAtUtc
        };
    }

    private static VersionSummary MapToVersionSummary(StoredVersionSummary Stored)
    {
        return new VersionSummary(
            new VersionId(Stored.VersionId),
            Stored.DisplayName,
            Stored.IsRelease,
            Stored.ReleasedAtUtc);
    }

    private static StoredLoaderVersionSummary MapFromLoaderVersionSummary(LoaderVersionSummary Summary)
    {
        return new StoredLoaderVersionSummary
        {
            LoaderType = Summary.LoaderType,
            GameVersion = Summary.GameVersion.ToString() ?? string.Empty,
            LoaderVersion = Summary.LoaderVersion,
            IsStable = Summary.IsStable
        };
    }

    private static LoaderVersionSummary MapToLoaderVersionSummary(StoredLoaderVersionSummary Stored)
    {
        return new LoaderVersionSummary(
            Stored.LoaderType,
            new VersionId(Stored.GameVersion),
            Stored.LoaderVersion,
            Stored.IsStable);
    }
}