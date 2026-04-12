using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Infrastructure.Metadata.Clients;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Catalog;

public sealed class CurseForgeModpackPreflightResult
{
    public IReadOnlyList<CurseForgeResolvedDownloadableFile> DownloadableFiles { get; init; } = [];
    public IReadOnlyList<PendingManualDownloadFile> BlockedFiles { get; init; } = [];
}

public sealed class CurseForgeResolvedDownloadableFile
{
    public CatalogFileSummary File { get; init; } = default!;
    public string DestinationRelativePath { get; init; } = string.Empty;
}

public sealed class CurseForgeModpackPreflightService
{
    private readonly CurseForgeContentCatalogService curseForgeContentCatalogService;
    private readonly IContentCatalogFileService contentCatalogFileService;

    public CurseForgeModpackPreflightService(
        CurseForgeContentCatalogService curseForgeContentCatalogService,
        IContentCatalogFileService contentCatalogFileService)
    {
        this.curseForgeContentCatalogService = curseForgeContentCatalogService ?? throw new ArgumentNullException(nameof(curseForgeContentCatalogService));
        this.contentCatalogFileService = contentCatalogFileService ?? throw new ArgumentNullException(nameof(contentCatalogFileService));
    }

    internal async Task<Result<CurseForgeModpackPreflightResult>> ExecuteAsync(
        CurseForgeModpackManifest manifest,
        string? requestedLoader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var projectIdsByFileId = manifest.Files
            .GroupBy(static file => file.FileId.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.First().ProjectId.ToString(),
                StringComparer.OrdinalIgnoreCase);

        var exactFilesResult = await curseForgeContentCatalogService
            .GetFilesByIdsAsync(projectIdsByFileId, CatalogContentType.Mod, cancellationToken)
            .ConfigureAwait(false);
        if (exactFilesResult.IsFailure)
        {
            return Result<CurseForgeModpackPreflightResult>.Failure(exactFilesResult.Error);
        }

        var projectMetadataResult = await curseForgeContentCatalogService
            .GetProjectsByIdsAsync(projectIdsByFileId.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), cancellationToken)
            .ConfigureAwait(false);
        if (projectMetadataResult.IsFailure)
        {
            return Result<CurseForgeModpackPreflightResult>.Failure(projectMetadataResult.Error);
        }

        var downloadableFiles = new List<CurseForgeResolvedDownloadableFile>();
        var blockedFiles = new List<PendingManualDownloadFile>();

        foreach (var reference in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var projectId = reference.ProjectId.ToString();
            var manifestFileId = reference.FileId.ToString();
            exactFilesResult.Value.TryGetValue(manifestFileId, out var exactFile);
            projectMetadataResult.Value.TryGetValue(projectId, out var projectMetadata);

            if (exactFile is not null)
            {
                exactFile = ApplyProjectMetadata(exactFile, projectMetadata);
            }

            if (exactFile is not null &&
                !exactFile.RequiresManualDownload &&
                !string.IsNullOrWhiteSpace(exactFile.DownloadUrl))
            {
                downloadableFiles.Add(new CurseForgeResolvedDownloadableFile
                {
                    File = exactFile,
                    DestinationRelativePath = CatalogInstallSupport.ResolveInstalledRelativePath(CatalogContentType.Mod, exactFile.FileName)
                });
                continue;
            }

            var manualTarget = await ResolveManualTargetAsync(
                projectId,
                exactFile,
                manifest.Minecraft.Version,
                requestedLoader,
                projectMetadata,
                cancellationToken).ConfigureAwait(false);

            if (manualTarget is null)
            {
                return Result<CurseForgeModpackPreflightResult>.Failure(new Error(
                    "Catalog.CurseForgeManualFileUnavailable",
                    $"Could not resolve a compatible manual download target for project {projectId}."));
            }

            blockedFiles.Add(new PendingManualDownloadFile
            {
                Provider = CatalogProvider.CurseForge,
                ContentType = CatalogContentType.Mod,
                ProjectId = projectId,
                ProjectName = projectMetadata?.Title,
                IconUrl = manualTarget.IconUrl ?? projectMetadata?.IconUrl,
                FileId = manualTarget.FileId,
                ManifestFileId = exactFile?.FileId ?? manifestFileId,
                DisplayName = !string.IsNullOrWhiteSpace(projectMetadata?.Title) ? projectMetadata.Title : manualTarget.DisplayName,
                FileName = manualTarget.FileName,
                ManifestFileName = exactFile?.FileName,
                DestinationRelativePath = CatalogInstallSupport.ResolveInstalledRelativePath(CatalogContentType.Mod, manualTarget.FileName),
                DirectDownloadUrl = CatalogInstallSupport.ResolveManualDownloadUrl(manualTarget),
                ProjectUrl = manualTarget.ProjectUrl,
                FilePageUrl = CatalogInstallSupport.ResolveManualDownloadFilePageUrl(manualTarget),
                Sha1 = manualTarget.Sha1,
                ManifestSha1 = exactFile?.Sha1,
                SizeBytes = manualTarget.SizeBytes,
                ManifestSizeBytes = exactFile?.SizeBytes ?? 0
            });
        }

        return Result<CurseForgeModpackPreflightResult>.Success(new CurseForgeModpackPreflightResult
        {
            DownloadableFiles = downloadableFiles,
            BlockedFiles = blockedFiles
        });
    }

    private async Task<CatalogFileSummary?> ResolveManualTargetAsync(
        string projectId,
        CatalogFileSummary? exactFile,
        string gameVersion,
        string? loader,
        CurseForgeProjectMetadata? projectMetadata,
        CancellationToken cancellationToken)
    {
        if (exactFile is not null)
        {
            var preferredFile = await CatalogInstallSupport.ResolvePreferredCurseForgeManualFileAsync(
                contentCatalogFileService,
                exactFile,
                gameVersion,
                loader,
                cancellationToken).ConfigureAwait(false);

            return ApplyProjectMetadata(preferredFile, projectMetadata);
        }

        var compatibleResult = await contentCatalogFileService.ResolveFileAsync(new CatalogFileResolutionQuery
        {
            Provider = CatalogProvider.CurseForge,
            ContentType = CatalogContentType.Mod,
            ProjectId = projectId,
            GameVersion = gameVersion,
            GameVersions = [gameVersion],
            Loader = loader,
            Loaders = string.IsNullOrWhiteSpace(loader) ? [] : [loader]
        }, cancellationToken).ConfigureAwait(false);

        if (compatibleResult.IsFailure)
        {
            return null;
        }

        return ApplyProjectMetadata(compatibleResult.Value, projectMetadata);
    }

    private static CatalogFileSummary ApplyProjectMetadata(
        CatalogFileSummary file,
        CurseForgeProjectMetadata? metadata)
    {
        if (metadata is null)
        {
            return file;
        }

        return new CatalogFileSummary
        {
            Provider = file.Provider,
            ContentType = file.ContentType,
            ProjectId = file.ProjectId,
            FileId = file.FileId,
            DisplayName = string.IsNullOrWhiteSpace(file.DisplayName) ? metadata.Title : file.DisplayName,
            FileName = file.FileName,
            IconUrl = string.IsNullOrWhiteSpace(file.IconUrl) ? metadata.IconUrl : file.IconUrl,
            DownloadUrl = file.DownloadUrl,
            ProjectUrl = string.IsNullOrWhiteSpace(file.ProjectUrl) ? metadata.WebsiteUrl : file.ProjectUrl,
            FilePageUrl = string.IsNullOrWhiteSpace(file.FilePageUrl)
                ? CatalogInstallSupport.ResolveManualDownloadFilePageUrl(new CatalogFileSummary
                {
                    Provider = file.Provider,
                    ContentType = file.ContentType,
                    ProjectId = file.ProjectId,
                    FileId = file.FileId,
                    DisplayName = file.DisplayName,
                    FileName = file.FileName,
                    IconUrl = string.IsNullOrWhiteSpace(file.IconUrl) ? metadata.IconUrl : file.IconUrl,
                    DownloadUrl = file.DownloadUrl,
                    ProjectUrl = metadata.WebsiteUrl,
                    Sha1 = file.Sha1,
                    SizeBytes = file.SizeBytes,
                    PublishedAtUtc = file.PublishedAtUtc,
                    GameVersions = file.GameVersions,
                    Loaders = file.Loaders,
                    IsServerPack = file.IsServerPack,
                    RequiresManualDownload = file.RequiresManualDownload
                })
                : file.FilePageUrl,
            Sha1 = file.Sha1,
            SizeBytes = file.SizeBytes,
            PublishedAtUtc = file.PublishedAtUtc,
            GameVersions = file.GameVersions,
            Loaders = file.Loaders,
            IsServerPack = file.IsServerPack,
            RequiresManualDownload = file.RequiresManualDownload
        };
    }
}
