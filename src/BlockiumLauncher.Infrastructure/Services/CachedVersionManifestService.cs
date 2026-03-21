using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Metadata;
using BlockiumLauncher.Infrastructure.Metadata.Clients;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Services;

public sealed class CachedVersionManifestService : IVersionManifestService
{
    private readonly IMetadataCacheRepository MetadataCacheRepository;
    private readonly ILauncherPaths LauncherPaths;
    private readonly MojangVersionManifestClient MojangVersionManifestClient;
    private readonly MetadataCachePolicy CachePolicy;

    public CachedVersionManifestService(
        IMetadataCacheRepository MetadataCacheRepository,
        ILauncherPaths LauncherPaths,
        MojangVersionManifestClient MojangVersionManifestClient,
        MetadataCachePolicy CachePolicy)
    {
        this.MetadataCacheRepository = MetadataCacheRepository;
        this.LauncherPaths = LauncherPaths;
        this.MojangVersionManifestClient = MojangVersionManifestClient;
        this.CachePolicy = CachePolicy;
    }

    public async Task<Result<IReadOnlyList<VersionSummary>>> GetAvailableVersionsAsync(CancellationToken CancellationToken)
    {
        var CachePath = LauncherPaths.VersionsCacheFilePath;
        var NowUtc = DateTimeOffset.UtcNow;
        var CachedVersions = await MetadataCacheRepository.GetCachedVersionsAsync(CancellationToken);
        var CacheTimestampUtc = TryGetFileTimestampUtc(CachePath);

        if (CachedVersions is not null
            && CacheTimestampUtc.HasValue
            && CachePolicy.IsFresh(CacheTimestampUtc.Value, NowUtc)) {
            return Result<IReadOnlyList<VersionSummary>>.Success(CachedVersions);
        }

        var RemoteVersions = await MojangVersionManifestClient.GetVersionsAsync(CancellationToken);

        if (RemoteVersions.IsSuccess) {
            await MetadataCacheRepository.SaveCachedVersionsAsync(RemoteVersions.Value, CancellationToken);
            return Result<IReadOnlyList<VersionSummary>>.Success(RemoteVersions.Value);
        }

        if (CachedVersions is not null
            && CacheTimestampUtc.HasValue
            && CachePolicy.CanUseStaleFallback(CacheTimestampUtc.Value, NowUtc)) {
            return Result<IReadOnlyList<VersionSummary>>.Success(CachedVersions);
        }

        return Result<IReadOnlyList<VersionSummary>>.Failure(RemoteVersions.Error);
    }

    public async Task<Result<VersionSummary?>> GetVersionAsync(VersionId VersionId, CancellationToken CancellationToken)
    {
        var VersionsResult = await GetAvailableVersionsAsync(CancellationToken);

        if (VersionsResult.IsFailure) {
            return Result<VersionSummary?>.Failure(VersionsResult.Error);
        }

        var Version = VersionsResult.Value.FirstOrDefault(Item => Item.VersionId.Equals(VersionId));
        return Result<VersionSummary?>.Success(Version);
    }

    private static DateTimeOffset? TryGetFileTimestampUtc(string FilePath)
    {
        if (!File.Exists(FilePath)) {
            return null;
        }

        return File.GetLastWriteTimeUtc(FilePath);
    }
}





