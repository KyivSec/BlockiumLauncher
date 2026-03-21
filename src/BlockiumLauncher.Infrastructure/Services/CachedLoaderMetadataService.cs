using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Metadata;
using BlockiumLauncher.Infrastructure.Metadata.Clients;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Services;

public sealed class CachedLoaderMetadataService : ILoaderMetadataService
{
    private readonly IMetadataCacheRepository MetadataCacheRepository;
    private readonly ILauncherPaths LauncherPaths;
    private readonly FabricMetadataClient FabricMetadataClient;
    private readonly QuiltMetadataClient QuiltMetadataClient;
    private readonly ForgeMetadataClient ForgeMetadataClient;
    private readonly NeoForgeMetadataClient NeoForgeMetadataClient;
    private readonly MetadataCachePolicy CachePolicy;

    public CachedLoaderMetadataService(
        IMetadataCacheRepository MetadataCacheRepository,
        ILauncherPaths LauncherPaths,
        FabricMetadataClient FabricMetadataClient,
        QuiltMetadataClient QuiltMetadataClient,
        ForgeMetadataClient ForgeMetadataClient,
        NeoForgeMetadataClient NeoForgeMetadataClient,
        MetadataCachePolicy CachePolicy)
    {
        this.MetadataCacheRepository = MetadataCacheRepository;
        this.LauncherPaths = LauncherPaths;
        this.FabricMetadataClient = FabricMetadataClient;
        this.QuiltMetadataClient = QuiltMetadataClient;
        this.ForgeMetadataClient = ForgeMetadataClient;
        this.NeoForgeMetadataClient = NeoForgeMetadataClient;
        this.CachePolicy = CachePolicy;
    }

    public async Task<Result<IReadOnlyList<LoaderVersionSummary>>> GetLoaderVersionsAsync(
        LoaderType LoaderType,
        VersionId GameVersion,
        CancellationToken CancellationToken)
    {
        if (LoaderType == LoaderType.Vanilla) {
            return Result<IReadOnlyList<LoaderVersionSummary>>.Failure(
                MetadataErrors.UnsupportedLoaderType("Vanilla does not have separate loader metadata."));
        }

        var CachePath = LauncherPaths.GetLoaderVersionsCacheFilePath(LoaderType, GameVersion);
        var NowUtc = DateTimeOffset.UtcNow;
        var CachedVersions = await MetadataCacheRepository.GetCachedLoaderVersionsAsync(
            LoaderType,
            GameVersion,
            CancellationToken);

        var CacheTimestampUtc = TryGetFileTimestampUtc(CachePath);

        if (CachedVersions is not null
            && CacheTimestampUtc.HasValue
            && CachePolicy.IsFresh(CacheTimestampUtc.Value, NowUtc)) {
            return Result<IReadOnlyList<LoaderVersionSummary>>.Success(CachedVersions);
        }

        var RemoteVersions = await FetchRemoteVersionsAsync(LoaderType, GameVersion, CancellationToken);

        if (RemoteVersions.IsSuccess) {
            await MetadataCacheRepository.SaveCachedLoaderVersionsAsync(
                LoaderType,
                GameVersion,
                RemoteVersions.Value,
                CancellationToken);

            return Result<IReadOnlyList<LoaderVersionSummary>>.Success(RemoteVersions.Value);
        }

        if (CachedVersions is not null
            && CacheTimestampUtc.HasValue
            && CachePolicy.CanUseStaleFallback(CacheTimestampUtc.Value, NowUtc)) {
            return Result<IReadOnlyList<LoaderVersionSummary>>.Success(CachedVersions);
        }

        return Result<IReadOnlyList<LoaderVersionSummary>>.Failure(RemoteVersions.Error);
    }

    private Task<Result<IReadOnlyList<LoaderVersionSummary>>> FetchRemoteVersionsAsync(
        LoaderType LoaderType,
        VersionId GameVersion,
        CancellationToken CancellationToken)
    {
        return LoaderType switch
        {
            LoaderType.Fabric => FabricMetadataClient.GetLoaderVersionsAsync(GameVersion, CancellationToken),
            LoaderType.Quilt => QuiltMetadataClient.GetLoaderVersionsAsync(GameVersion, CancellationToken),
            LoaderType.Forge => ForgeMetadataClient.GetLoaderVersionsAsync(GameVersion, CancellationToken),
            LoaderType.NeoForge => NeoForgeMetadataClient.GetLoaderVersionsAsync(GameVersion, CancellationToken),
            _ => Task.FromResult(
                Result<IReadOnlyList<LoaderVersionSummary>>.Failure(
                    MetadataErrors.UnsupportedLoaderType(
                        $"Unsupported loader type '{LoaderType}'.")))
        };
    }

    private static DateTimeOffset? TryGetFileTimestampUtc(string FilePath)
    {
        if (!File.Exists(FilePath)) {
            return null;
        }

        return File.GetLastWriteTimeUtc(FilePath);
    }
}







