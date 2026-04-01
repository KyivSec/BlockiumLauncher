using System.Text.Json;
using System.Text.Json.Serialization;
using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Downloads;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Catalog;

public static class CatalogFileErrors
{
    public static readonly Error InvalidRequest = new("Catalog.InvalidRequest", "The catalog file request is invalid.");
    public static readonly Error InstanceNotFound = new("Catalog.InstanceNotFound", "The requested instance was not found.");
    public static readonly Error UnsupportedContentType = new("Catalog.UnsupportedContentType", "The requested catalog content type is not supported by this command.");
    public static readonly Error FileAlreadyExists = new("Catalog.FileAlreadyExists", "A file with the same name already exists in the target instance.");
    public static readonly Error DownloadUrlMissing = new("Catalog.DownloadUrlMissing", "The provider did not return a downloadable URL for the requested file.");
    public static readonly Error ModpackManifestInvalid = new("Catalog.ModpackManifestInvalid", "The downloaded CurseForge modpack does not contain a valid manifest.json.");
    public static readonly Error ModpackLoaderUnsupported = new("Catalog.ModpackLoaderUnsupported", "The CurseForge modpack uses a loader that is not currently supported.");
    public static readonly Error NoPendingManualDownloads = new("Catalog.NoPendingManualDownloads", "The requested instance does not have any pending manual downloads.");
}

public sealed class ListCatalogFilesRequest
{
    public CatalogProvider Provider { get; init; } = CatalogProvider.CurseForge;
    public CatalogContentType ContentType { get; init; }
    public string ProjectId { get; init; } = string.Empty;
    public string? GameVersion { get; init; }
    public string? Loader { get; init; }
    public int Limit { get; init; } = 20;
    public int Offset { get; init; }
}

public sealed class InstallCatalogContentRequest
{
    public CatalogProvider Provider { get; init; } = CatalogProvider.CurseForge;
    public CatalogContentType ContentType { get; init; }
    public InstanceId InstanceId { get; init; }
    public string ProjectId { get; init; } = string.Empty;
    public string? FileId { get; init; }
    public string? GameVersion { get; init; }
    public string? Loader { get; init; }
    public bool OverwriteExisting { get; init; }
}

public sealed class InstallCatalogContentResult
{
    public LauncherInstance Instance { get; init; } = default!;
    public CatalogFileSummary File { get; init; } = default!;
    public string InstalledPath { get; init; } = string.Empty;
    public InstanceContentMetadata Metadata { get; init; } = default!;
}

public sealed class ImportCatalogModpackRequest
{
    public CatalogProvider Provider { get; init; } = CatalogProvider.CurseForge;
    public string ProjectId { get; init; } = string.Empty;
    public string? FileId { get; init; }
    public string InstanceName { get; init; } = string.Empty;
    public string? TargetDirectory { get; init; }
    public bool OverwriteIfExists { get; init; }
    public bool DownloadRuntime { get; init; }
    public string? DownloadsDirectory { get; init; }
    public bool WaitForManualDownloads { get; init; }
    public TimeSpan WaitTimeout { get; init; } = TimeSpan.FromMinutes(30);
}

public sealed class ImportCatalogModpackResult
{
    public LauncherInstance Instance { get; init; } = default!;
    public CatalogFileSummary File { get; init; } = default!;
    public string InstalledPath { get; init; } = string.Empty;
    public InstanceContentMetadata Metadata { get; init; } = default!;
    public string DownloadsDirectory { get; init; } = string.Empty;
    public IReadOnlyList<PendingManualDownloadFile> PendingManualDownloads { get; init; } = [];
    public bool IsCompleted => PendingManualDownloads.Count == 0;
}

public sealed class ListCatalogFilesUseCase
{
    private readonly IContentCatalogFileService contentCatalogFileService;

    public ListCatalogFilesUseCase(IContentCatalogFileService contentCatalogFileService)
    {
        this.contentCatalogFileService = contentCatalogFileService ?? throw new ArgumentNullException(nameof(contentCatalogFileService));
    }

    public Task<Result<IReadOnlyList<CatalogFileSummary>>> ExecuteAsync(
        ListCatalogFilesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            request.Limit <= 0 ||
            request.Limit > 50 ||
            request.Offset < 0)
        {
            return Task.FromResult(Result<IReadOnlyList<CatalogFileSummary>>.Failure(CatalogFileErrors.InvalidRequest));
        }

        return contentCatalogFileService.GetFilesAsync(new CatalogFileQuery
        {
            Provider = request.Provider,
            ContentType = request.ContentType,
            ProjectId = request.ProjectId,
            GameVersion = request.GameVersion,
            Loader = request.Loader,
            Limit = request.Limit,
            Offset = request.Offset
        }, cancellationToken);
    }
}

public sealed class InstallCatalogContentUseCase
{
    private readonly IInstanceRepository instanceRepository;
    private readonly IContentCatalogFileService contentCatalogFileService;
    private readonly IDownloader downloader;
    private readonly ITempWorkspaceFactory tempWorkspaceFactory;
    private readonly IInstanceContentMetadataService instanceContentMetadataService;

    public InstallCatalogContentUseCase(
        IInstanceRepository instanceRepository,
        IContentCatalogFileService contentCatalogFileService,
        IDownloader downloader,
        ITempWorkspaceFactory tempWorkspaceFactory,
        IInstanceContentMetadataService instanceContentMetadataService)
    {
        this.instanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
        this.contentCatalogFileService = contentCatalogFileService ?? throw new ArgumentNullException(nameof(contentCatalogFileService));
        this.downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        this.tempWorkspaceFactory = tempWorkspaceFactory ?? throw new ArgumentNullException(nameof(tempWorkspaceFactory));
        this.instanceContentMetadataService = instanceContentMetadataService ?? throw new ArgumentNullException(nameof(instanceContentMetadataService));
    }

    public async Task<Result<InstallCatalogContentResult>> ExecuteAsync(
        InstallCatalogContentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null ||
            request.InstanceId == default ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            request.ContentType == CatalogContentType.Modpack)
        {
            return Result<InstallCatalogContentResult>.Failure(
                request?.ContentType == CatalogContentType.Modpack
                    ? CatalogFileErrors.UnsupportedContentType
                    : CatalogFileErrors.InvalidRequest);
        }

        var instance = await instanceRepository.GetByIdAsync(request.InstanceId, cancellationToken).ConfigureAwait(false);
        if (instance is null)
        {
            return Result<InstallCatalogContentResult>.Failure(CatalogFileErrors.InstanceNotFound);
        }

        var resolvedFileResult = await contentCatalogFileService.ResolveFileAsync(new CatalogFileResolutionQuery
        {
            Provider = request.Provider,
            ContentType = request.ContentType,
            ProjectId = request.ProjectId,
            FileId = request.FileId,
            GameVersion = request.GameVersion ?? instance.GameVersion.ToString(),
            Loader = request.Loader ?? CatalogInstallSupport.GetLoaderText(instance.LoaderType)
        }, cancellationToken).ConfigureAwait(false);

        if (resolvedFileResult.IsFailure)
        {
            return Result<InstallCatalogContentResult>.Failure(resolvedFileResult.Error);
        }

        var resolvedFile = resolvedFileResult.Value;
        if (string.IsNullOrWhiteSpace(resolvedFile.DownloadUrl))
        {
            return Result<InstallCatalogContentResult>.Failure(CatalogFileErrors.DownloadUrlMissing);
        }

        await using var workspace = await tempWorkspaceFactory.CreateAsync("catalog-content", cancellationToken).ConfigureAwait(false);
        var tempPath = workspace.GetPath(Path.Combine("downloads", resolvedFile.FileName));
        var downloadResult = await downloader.DownloadAsync(
            new DownloadRequest(new Uri(resolvedFile.DownloadUrl), tempPath, resolvedFile.Sha1),
            cancellationToken).ConfigureAwait(false);

        if (downloadResult.IsFailure)
        {
            return Result<InstallCatalogContentResult>.Failure(downloadResult.Error);
        }

        var relativePath = CatalogInstallSupport.ResolveInstalledRelativePath(request.ContentType, resolvedFile.FileName);
        var destinationPath = Path.Combine(instance.InstallLocation, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        if (File.Exists(destinationPath) && !request.OverwriteExisting)
        {
            return Result<InstallCatalogContentResult>.Failure(CatalogFileErrors.FileAlreadyExists);
        }

        File.Copy(downloadResult.Value.DestinationPath, destinationPath, overwrite: true);

        var metadata = await instanceContentMetadataService.ApplySourcesAsync(
            instance,
            new Dictionary<string, ContentSourceMetadata>(StringComparer.OrdinalIgnoreCase)
            {
                [relativePath] = CatalogInstallSupport.BuildSourceMetadata(resolvedFile)
            },
            cancellationToken).ConfigureAwait(false);

        return Result<InstallCatalogContentResult>.Success(new InstallCatalogContentResult
        {
            Instance = instance,
            File = resolvedFile,
            InstalledPath = destinationPath,
            Metadata = metadata
        });
    }
}

public sealed class ImportCatalogModpackUseCase
{
    private static readonly TimeSpan ManualDownloadPollInterval = TimeSpan.FromSeconds(2);

    private readonly IContentCatalogFileService contentCatalogFileService;
    private readonly IDownloader downloader;
    private readonly ITempWorkspaceFactory tempWorkspaceFactory;
    private readonly IArchiveExtractor archiveExtractor;
    private readonly InstallInstanceUseCase installInstanceUseCase;
    private readonly IInstanceRepository instanceRepository;
    private readonly IInstanceContentMetadataService instanceContentMetadataService;
    private readonly IManualDownloadStateStore manualDownloadStateStore;

    public ImportCatalogModpackUseCase(
        IContentCatalogFileService contentCatalogFileService,
        IDownloader downloader,
        ITempWorkspaceFactory tempWorkspaceFactory,
        IArchiveExtractor archiveExtractor,
        InstallInstanceUseCase installInstanceUseCase,
        IInstanceRepository instanceRepository,
        IInstanceContentMetadataService instanceContentMetadataService,
        IManualDownloadStateStore manualDownloadStateStore)
    {
        this.contentCatalogFileService = contentCatalogFileService ?? throw new ArgumentNullException(nameof(contentCatalogFileService));
        this.downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        this.tempWorkspaceFactory = tempWorkspaceFactory ?? throw new ArgumentNullException(nameof(tempWorkspaceFactory));
        this.archiveExtractor = archiveExtractor ?? throw new ArgumentNullException(nameof(archiveExtractor));
        this.installInstanceUseCase = installInstanceUseCase ?? throw new ArgumentNullException(nameof(installInstanceUseCase));
        this.instanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
        this.instanceContentMetadataService = instanceContentMetadataService ?? throw new ArgumentNullException(nameof(instanceContentMetadataService));
        this.manualDownloadStateStore = manualDownloadStateStore ?? throw new ArgumentNullException(nameof(manualDownloadStateStore));
    }

    public async Task<Result<ImportCatalogModpackResult>> ExecuteAsync(
        ImportCatalogModpackRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.InstanceName))
        {
            return Result<ImportCatalogModpackResult>.Failure(CatalogFileErrors.InvalidRequest);
        }

        await using var workspace = await tempWorkspaceFactory.CreateAsync("catalog-modpack", cancellationToken).ConfigureAwait(false);
        var downloadsDirectory = CatalogInstallSupport.ResolveDownloadsDirectory(request.DownloadsDirectory);

        var modpackFileResult = await contentCatalogFileService.ResolveFileAsync(new CatalogFileResolutionQuery
        {
            Provider = request.Provider,
            ContentType = CatalogContentType.Modpack,
            ProjectId = request.ProjectId,
            FileId = request.FileId
        }, cancellationToken).ConfigureAwait(false);

        if (modpackFileResult.IsFailure)
        {
            return Result<ImportCatalogModpackResult>.Failure(modpackFileResult.Error);
        }

        var modpackFile = modpackFileResult.Value;
        if (string.IsNullOrWhiteSpace(modpackFile.DownloadUrl))
        {
            return Result<ImportCatalogModpackResult>.Failure(CatalogFileErrors.DownloadUrlMissing);
        }

        var archivePath = workspace.GetPath("modpack.zip");
        var archiveDownloadResult = await downloader.DownloadAsync(
            new DownloadRequest(new Uri(modpackFile.DownloadUrl), archivePath, modpackFile.Sha1),
            cancellationToken).ConfigureAwait(false);

        if (archiveDownloadResult.IsFailure)
        {
            return Result<ImportCatalogModpackResult>.Failure(archiveDownloadResult.Error);
        }

        var extractRoot = workspace.GetPath("extracted");
        var extractResult = await archiveExtractor.ExtractAsync(archivePath, extractRoot, cancellationToken).ConfigureAwait(false);
        if (extractResult.IsFailure)
        {
            return Result<ImportCatalogModpackResult>.Failure(extractResult.Error);
        }

        var importContextResult = request.Provider switch
        {
            CatalogProvider.CurseForge => await ImportCurseForgeArchiveAsync(
                extractRoot,
                workspace,
                request,
                cancellationToken).ConfigureAwait(false),
            CatalogProvider.Modrinth => await ImportModrinthArchiveAsync(
                extractRoot,
                workspace,
                request,
                cancellationToken).ConfigureAwait(false),
            _ => Result<CatalogModpackImportContext>.Failure(CatalogFileErrors.ModpackManifestInvalid)
        };

        if (importContextResult.IsFailure)
        {
            return Result<ImportCatalogModpackResult>.Failure(importContextResult.Error);
        }

        var importContext = importContextResult.Value;
        var instance = importContext.Instance;
        var metadata = importContext.Metadata;
        var pendingManualDownloads = importContext.PendingManualDownloads;

        if (pendingManualDownloads.Count > 0)
        {
            await manualDownloadStateStore.SaveAsync(
                instance.InstallLocation,
                new PendingManualDownloadsState
                {
                    Provider = request.Provider,
                    ModpackProjectId = modpackFile.ProjectId,
                    ModpackFileId = modpackFile.FileId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Files = pendingManualDownloads
                },
                cancellationToken).ConfigureAwait(false);

            if (request.WaitForManualDownloads)
            {
                var resumeResult = await ResumePendingManualDownloadsAsync(
                    instance,
                    downloadsDirectory,
                    request.WaitTimeout,
                    waitForManualDownloads: true,
                    cancellationToken).ConfigureAwait(false);

                if (resumeResult.IsFailure)
                {
                    return Result<ImportCatalogModpackResult>.Failure(resumeResult.Error);
                }

                return Result<ImportCatalogModpackResult>.Success(new ImportCatalogModpackResult
                {
                    Instance = resumeResult.Value.Instance,
                    File = modpackFile,
                    InstalledPath = importContext.InstalledPath,
                    Metadata = await instanceContentMetadataService.GetAsync(instance, reindexIfMissing: true, cancellationToken).ConfigureAwait(false) ?? metadata,
                    DownloadsDirectory = resumeResult.Value.DownloadsDirectory,
                    PendingManualDownloads = resumeResult.Value.PendingManualDownloads
                });
            }
        }
        else
        {
            await manualDownloadStateStore.DeleteAsync(instance.InstallLocation, cancellationToken).ConfigureAwait(false);
        }

        return Result<ImportCatalogModpackResult>.Success(new ImportCatalogModpackResult
        {
            Instance = instance,
            File = modpackFile,
            InstalledPath = importContext.InstalledPath,
            Metadata = metadata,
            DownloadsDirectory = downloadsDirectory,
            PendingManualDownloads = pendingManualDownloads
        });
    }

    internal async Task<Result<ResumeCatalogModpackImportResult>> ResumePendingManualDownloadsAsync(
        LauncherInstance instance,
        string downloadsDirectory,
        TimeSpan waitTimeout,
        bool waitForManualDownloads,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;

        while (true)
        {
            var pendingState = await manualDownloadStateStore.LoadAsync(instance.InstallLocation, cancellationToken).ConfigureAwait(false);
            if (pendingState is null || pendingState.Files.Count == 0)
            {
                await manualDownloadStateStore.DeleteAsync(instance.InstallLocation, cancellationToken).ConfigureAwait(false);
                return Result<ResumeCatalogModpackImportResult>.Success(new ResumeCatalogModpackImportResult
                {
                    Instance = instance,
                    DownloadsDirectory = downloadsDirectory
                });
            }

            var importedFiles = new List<string>();
            var remainingFiles = new List<PendingManualDownloadFile>();
            var sources = new Dictionary<string, ContentSourceMetadata>(StringComparer.OrdinalIgnoreCase);

            foreach (var pendingFile in pendingState.Files)
            {
                var downloadedPath = CatalogInstallSupport.TryFindDownloadedFile(downloadsDirectory, pendingFile.FileName, pendingFile.SizeBytes);
                if (downloadedPath is null)
                {
                    remainingFiles.Add(pendingFile);
                    continue;
                }

                var destinationPath = Path.Combine(
                    instance.InstallLocation,
                    pendingFile.DestinationRelativePath.Replace('/', Path.DirectorySeparatorChar));
                var destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                File.Copy(downloadedPath, destinationPath, overwrite: true);
                importedFiles.Add(destinationPath);
                sources[pendingFile.DestinationRelativePath] = CatalogInstallSupport.BuildSourceMetadata(pendingFile);
            }

            if (sources.Count > 0)
            {
                await instanceContentMetadataService.ApplySourcesAsync(instance, sources, cancellationToken).ConfigureAwait(false);
            }

            if (remainingFiles.Count == 0)
            {
                await manualDownloadStateStore.DeleteAsync(instance.InstallLocation, cancellationToken).ConfigureAwait(false);
                return Result<ResumeCatalogModpackImportResult>.Success(new ResumeCatalogModpackImportResult
                {
                    Instance = instance,
                    DownloadsDirectory = downloadsDirectory,
                    ImportedFiles = importedFiles
                });
            }

            await manualDownloadStateStore.SaveAsync(
                instance.InstallLocation,
                new PendingManualDownloadsState
                {
                    Provider = pendingState.Provider,
                    ModpackProjectId = pendingState.ModpackProjectId,
                    ModpackFileId = pendingState.ModpackFileId,
                    CreatedAtUtc = pendingState.CreatedAtUtc,
                    Files = remainingFiles
                },
                cancellationToken).ConfigureAwait(false);

            if (!waitForManualDownloads ||
                waitTimeout <= TimeSpan.Zero ||
                DateTimeOffset.UtcNow - startedAtUtc >= waitTimeout)
            {
                return Result<ResumeCatalogModpackImportResult>.Success(new ResumeCatalogModpackImportResult
                {
                    Instance = instance,
                    DownloadsDirectory = downloadsDirectory,
                    ImportedFiles = importedFiles,
                    PendingManualDownloads = remainingFiles
                });
            }

            await Task.Delay(ManualDownloadPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static async Task<Result<CurseForgeModpackManifest>> ReadManifestAsync(string extractRoot, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(extractRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return Result<CurseForgeModpackManifest>.Failure(CatalogFileErrors.ModpackManifestInvalid);
        }

        try
        {
            var json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<CurseForgeModpackManifest>(json);
            if (manifest is null ||
                manifest.Minecraft is null ||
                string.IsNullOrWhiteSpace(manifest.Minecraft.Version))
            {
                return Result<CurseForgeModpackManifest>.Failure(CatalogFileErrors.ModpackManifestInvalid);
            }

            return Result<CurseForgeModpackManifest>.Success(manifest);
        }
        catch (JsonException)
        {
            return Result<CurseForgeModpackManifest>.Failure(CatalogFileErrors.ModpackManifestInvalid);
        }
    }

    private async Task<Result<CatalogModpackImportContext>> ImportCurseForgeArchiveAsync(
        string extractRoot,
        ITempWorkspace workspace,
        ImportCatalogModpackRequest request,
        CancellationToken cancellationToken)
    {
        var manifestResult = await ReadManifestAsync(extractRoot, cancellationToken).ConfigureAwait(false);
        if (manifestResult.IsFailure)
        {
            return Result<CatalogModpackImportContext>.Failure(manifestResult.Error);
        }

        var manifest = manifestResult.Value;
        if (!TryResolveLoader(manifest, out var loaderType, out var loaderVersion))
        {
            return Result<CatalogModpackImportContext>.Failure(CatalogFileErrors.ModpackLoaderUnsupported);
        }

        var installResult = await installInstanceUseCase.ExecuteAsync(new InstallInstanceRequest
        {
            InstanceName = request.InstanceName,
            GameVersion = manifest.Minecraft.Version,
            LoaderType = loaderType,
            LoaderVersion = loaderVersion,
            TargetDirectory = request.TargetDirectory,
            OverwriteIfExists = request.OverwriteIfExists,
            DownloadRuntime = request.DownloadRuntime
        }, cancellationToken).ConfigureAwait(false);

        if (installResult.IsFailure)
        {
            return Result<CatalogModpackImportContext>.Failure(installResult.Error);
        }

        var stageRoot = workspace.GetPath("content-stage");
        Directory.CreateDirectory(stageRoot);

        var overridesRoot = string.IsNullOrWhiteSpace(manifest.Overrides)
            ? Path.Combine(extractRoot, "overrides")
            : Path.Combine(extractRoot, manifest.Overrides);

        if (Directory.Exists(overridesRoot))
        {
            CatalogInstallSupport.CopyDirectoryContents(overridesRoot, stageRoot, overwrite: true);
        }

        var sources = new Dictionary<string, ContentSourceMetadata>(StringComparer.OrdinalIgnoreCase);
        var pendingManualDownloads = new List<PendingManualDownloadFile>();
        foreach (var fileReference in manifest.Files)
        {
            var fileResult = await contentCatalogFileService.ResolveFileAsync(new CatalogFileResolutionQuery
            {
                Provider = request.Provider,
                ContentType = CatalogContentType.Mod,
                ProjectId = fileReference.ProjectId.ToString(),
                FileId = fileReference.FileId.ToString()
            }, cancellationToken).ConfigureAwait(false);

            if (fileResult.IsFailure)
            {
                return Result<CatalogModpackImportContext>.Failure(fileResult.Error);
            }

            var file = fileResult.Value;
            var relativePath = CatalogInstallSupport.ResolveInstalledRelativePath(CatalogContentType.Mod, file.FileName);

            if (file.RequiresManualDownload || string.IsNullOrWhiteSpace(file.DownloadUrl))
            {
                pendingManualDownloads.Add(new PendingManualDownloadFile
                {
                    Provider = file.Provider,
                    ContentType = CatalogContentType.Mod,
                    ProjectId = file.ProjectId,
                    FileId = file.FileId,
                    DisplayName = file.DisplayName,
                    FileName = file.FileName,
                    DestinationRelativePath = relativePath,
                    ProjectUrl = file.ProjectUrl,
                    FilePageUrl = file.FilePageUrl ?? file.ProjectUrl,
                    Sha1 = file.Sha1,
                    SizeBytes = file.SizeBytes
                });

                continue;
            }

            var stagedPath = workspace.GetPath(Path.Combine("content-stage", relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var downloadResult = await downloader.DownloadAsync(
                new DownloadRequest(new Uri(file.DownloadUrl), stagedPath, file.Sha1),
                cancellationToken).ConfigureAwait(false);

            if (downloadResult.IsFailure)
            {
                return Result<CatalogModpackImportContext>.Failure(downloadResult.Error);
            }

            sources[relativePath] = CatalogInstallSupport.BuildSourceMetadata(file);
        }

        CatalogInstallSupport.CopyDirectoryContents(stageRoot, installResult.Value.InstalledPath, overwrite: true);
        var metadata = await instanceContentMetadataService
            .ApplySourcesAsync(installResult.Value.Instance, sources, cancellationToken)
            .ConfigureAwait(false);

        return Result<CatalogModpackImportContext>.Success(new CatalogModpackImportContext
        {
            Instance = installResult.Value.Instance,
            InstalledPath = installResult.Value.InstalledPath,
            Metadata = metadata,
            PendingManualDownloads = pendingManualDownloads
        });
    }

    private async Task<Result<CatalogModpackImportContext>> ImportModrinthArchiveAsync(
        string extractRoot,
        ITempWorkspace workspace,
        ImportCatalogModpackRequest request,
        CancellationToken cancellationToken)
    {
        var manifestResult = await ReadModrinthManifestAsync(extractRoot, cancellationToken).ConfigureAwait(false);
        if (manifestResult.IsFailure)
        {
            return Result<CatalogModpackImportContext>.Failure(manifestResult.Error);
        }

        var manifest = manifestResult.Value;
        if (!TryResolveLoader(manifest, out var loaderType, out var loaderVersion))
        {
            return Result<CatalogModpackImportContext>.Failure(CatalogFileErrors.ModpackLoaderUnsupported);
        }

        var installResult = await installInstanceUseCase.ExecuteAsync(new InstallInstanceRequest
        {
            InstanceName = request.InstanceName,
            GameVersion = manifest.Dependencies.Minecraft,
            LoaderType = loaderType,
            LoaderVersion = loaderVersion,
            TargetDirectory = request.TargetDirectory,
            OverwriteIfExists = request.OverwriteIfExists,
            DownloadRuntime = request.DownloadRuntime
        }, cancellationToken).ConfigureAwait(false);

        if (installResult.IsFailure)
        {
            return Result<CatalogModpackImportContext>.Failure(installResult.Error);
        }

        var stageRoot = workspace.GetPath("content-stage");
        Directory.CreateDirectory(stageRoot);
        CopyModrinthOverrideDirectories(extractRoot, stageRoot);

        var sources = new Dictionary<string, ContentSourceMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            var downloadUrl = file.Downloads
                .FirstOrDefault(static item => Uri.TryCreate(item, UriKind.Absolute, out _));
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return Result<CatalogModpackImportContext>.Failure(CatalogFileErrors.DownloadUrlMissing);
            }

            var relativePath = CatalogInstallSupport.NormalizeRelativePath(file.Path);
            var stagedPath = workspace.GetPath(Path.Combine("content-stage", relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var downloadResult = await downloader.DownloadAsync(
                new DownloadRequest(new Uri(downloadUrl), stagedPath, file.Hashes.Sha1),
                cancellationToken).ConfigureAwait(false);

            if (downloadResult.IsFailure)
            {
                return Result<CatalogModpackImportContext>.Failure(downloadResult.Error);
            }

            var source = BuildModrinthSourceMetadata(relativePath, request.Provider, file.ProjectId, file.FileId, downloadUrl);
            if (source is not null)
            {
                sources[relativePath] = source;
            }
        }

        CatalogInstallSupport.CopyDirectoryContents(stageRoot, installResult.Value.InstalledPath, overwrite: true);
        var metadata = await instanceContentMetadataService
            .ApplySourcesAsync(installResult.Value.Instance, sources, cancellationToken)
            .ConfigureAwait(false);

        await manualDownloadStateStore.DeleteAsync(installResult.Value.Instance.InstallLocation, cancellationToken).ConfigureAwait(false);

        return Result<CatalogModpackImportContext>.Success(new CatalogModpackImportContext
        {
            Instance = installResult.Value.Instance,
            InstalledPath = installResult.Value.InstalledPath,
            Metadata = metadata,
            PendingManualDownloads = []
        });
    }

    internal static bool TryResolveLoader(CurseForgeModpackManifest manifest, out LoaderType loaderType, out string? loaderVersion)
    {
        loaderType = LoaderType.Vanilla;
        loaderVersion = null;

        var loaderId = manifest.Minecraft.ModLoaders
            .OrderByDescending(static item => item.Primary)
            .Select(static item => item.Id)
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

        if (string.IsNullOrWhiteSpace(loaderId))
        {
            return true;
        }

        var separatorIndex = loaderId.IndexOf('-', StringComparison.Ordinal);
        var loaderName = separatorIndex >= 0 ? loaderId[..separatorIndex] : loaderId;
        var version = separatorIndex >= 0 ? loaderId[(separatorIndex + 1)..] : null;

        switch (loaderName.Trim().ToLowerInvariant())
        {
            case "forge":
                loaderType = LoaderType.Forge;
                loaderVersion = version;
                return !string.IsNullOrWhiteSpace(loaderVersion);
            case "neoforge":
                loaderType = LoaderType.NeoForge;
                loaderVersion = version;
                return !string.IsNullOrWhiteSpace(loaderVersion);
            case "fabric":
                loaderType = LoaderType.Fabric;
                loaderVersion = version;
                return !string.IsNullOrWhiteSpace(loaderVersion);
            case "quilt":
                loaderType = LoaderType.Quilt;
                loaderVersion = version;
                return !string.IsNullOrWhiteSpace(loaderVersion);
            default:
                return false;
        }
    }

    internal static async Task<Result<ModrinthModpackManifest>> ReadModrinthManifestAsync(string extractRoot, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(extractRoot, "modrinth.index.json");
        if (!File.Exists(manifestPath))
        {
            return Result<ModrinthModpackManifest>.Failure(CatalogFileErrors.ModpackManifestInvalid);
        }

        try
        {
            var json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<ModrinthModpackManifest>(json);
            if (manifest is null ||
                manifest.Dependencies is null ||
                string.IsNullOrWhiteSpace(manifest.Dependencies.Minecraft))
            {
                return Result<ModrinthModpackManifest>.Failure(CatalogFileErrors.ModpackManifestInvalid);
            }

            return Result<ModrinthModpackManifest>.Success(manifest);
        }
        catch (JsonException)
        {
            return Result<ModrinthModpackManifest>.Failure(CatalogFileErrors.ModpackManifestInvalid);
        }
    }

    internal static bool TryResolveLoader(ModrinthModpackManifest manifest, out LoaderType loaderType, out string? loaderVersion)
    {
        loaderType = LoaderType.Vanilla;
        loaderVersion = null;

        if (!string.IsNullOrWhiteSpace(manifest.Dependencies.FabricLoader))
        {
            loaderType = LoaderType.Fabric;
            loaderVersion = manifest.Dependencies.FabricLoader;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(manifest.Dependencies.QuiltLoader))
        {
            loaderType = LoaderType.Quilt;
            loaderVersion = manifest.Dependencies.QuiltLoader;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(manifest.Dependencies.NeoForge))
        {
            loaderType = LoaderType.NeoForge;
            loaderVersion = manifest.Dependencies.NeoForge;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(manifest.Dependencies.Forge))
        {
            loaderType = LoaderType.Forge;
            loaderVersion = manifest.Dependencies.Forge;
            return true;
        }

        return true;
    }

    internal static void CopyModrinthOverrideDirectories(string extractRoot, string stageRoot)
    {
        foreach (var relativeDirectory in new[] { "overrides", "client-overrides" })
        {
            var fullPath = Path.Combine(extractRoot, relativeDirectory);
            if (Directory.Exists(fullPath))
            {
                CatalogInstallSupport.CopyDirectoryContents(fullPath, stageRoot, overwrite: true);
            }
        }
    }

    internal static ContentSourceMetadata? BuildModrinthSourceMetadata(
        string relativePath,
        CatalogProvider provider,
        string? projectId,
        string? fileId,
        string? originalUrl)
    {
        var contentType = relativePath.Replace('\\', '/').TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries) switch
        {
            ["mods", ..] => "mod",
            ["resourcepacks", ..] => "resourcepack",
            ["shaderpacks", ..] => "shader",
            _ => null
        };

        if (contentType is null)
        {
            return null;
        }

        return new ContentSourceMetadata
        {
            Provider = provider == CatalogProvider.Modrinth ? ContentOriginProvider.Modrinth : ContentOriginProvider.Unknown,
            ContentType = contentType,
            ProjectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId,
            FileId = string.IsNullOrWhiteSpace(fileId) ? null : fileId,
            OriginalUrl = originalUrl,
            AcquiredAtUtc = DateTimeOffset.UtcNow
        };
    }
}

public sealed class ResumeCatalogModpackImportUseCase
{
    private readonly IInstanceRepository instanceRepository;
    private readonly ImportCatalogModpackUseCase importCatalogModpackUseCase;
    private readonly IManualDownloadStateStore manualDownloadStateStore;

    public ResumeCatalogModpackImportUseCase(
        IInstanceRepository instanceRepository,
        ImportCatalogModpackUseCase importCatalogModpackUseCase,
        IManualDownloadStateStore manualDownloadStateStore)
    {
        this.instanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
        this.importCatalogModpackUseCase = importCatalogModpackUseCase ?? throw new ArgumentNullException(nameof(importCatalogModpackUseCase));
        this.manualDownloadStateStore = manualDownloadStateStore ?? throw new ArgumentNullException(nameof(manualDownloadStateStore));
    }

    public async Task<Result<ResumeCatalogModpackImportResult>> ExecuteAsync(
        ResumeCatalogModpackImportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null ||
            (string.IsNullOrWhiteSpace(request.InstanceId) && string.IsNullOrWhiteSpace(request.InstanceName)))
        {
            return Result<ResumeCatalogModpackImportResult>.Failure(CatalogFileErrors.InvalidRequest);
        }

        var instance = !string.IsNullOrWhiteSpace(request.InstanceId)
            ? await instanceRepository.GetByIdAsync(new InstanceId(request.InstanceId), cancellationToken).ConfigureAwait(false)
            : await instanceRepository.GetByNameAsync(request.InstanceName!, cancellationToken).ConfigureAwait(false);

        if (instance is null)
        {
            return Result<ResumeCatalogModpackImportResult>.Failure(CatalogFileErrors.InstanceNotFound);
        }

        var pendingState = await manualDownloadStateStore.LoadAsync(instance.InstallLocation, cancellationToken).ConfigureAwait(false);
        if (pendingState is null || pendingState.Files.Count == 0)
        {
            return Result<ResumeCatalogModpackImportResult>.Failure(CatalogFileErrors.NoPendingManualDownloads);
        }

        return await importCatalogModpackUseCase.ResumePendingManualDownloadsAsync(
            instance,
            CatalogInstallSupport.ResolveDownloadsDirectory(request.DownloadsDirectory),
            request.WaitTimeout,
            request.WaitForManualDownloads,
            cancellationToken).ConfigureAwait(false);
    }
}

internal static class CatalogInstallSupport
{
    internal static ContentSourceMetadata BuildSourceMetadata(CatalogFileSummary file)
    {
        return new ContentSourceMetadata
        {
            Provider = file.Provider switch
            {
                CatalogProvider.CurseForge => ContentOriginProvider.CurseForge,
                CatalogProvider.Modrinth => ContentOriginProvider.Modrinth,
                _ => ContentOriginProvider.Unknown
            },
            ContentType = ToSourceContentType(file.ContentType),
            ProjectId = file.ProjectId,
            FileId = file.FileId,
            OriginalUrl = file.DownloadUrl ?? file.FilePageUrl ?? file.ProjectUrl,
            AcquiredAtUtc = DateTimeOffset.UtcNow
        };
    }

    internal static ContentSourceMetadata BuildSourceMetadata(PendingManualDownloadFile file)
    {
        return new ContentSourceMetadata
        {
            Provider = file.Provider switch
            {
                CatalogProvider.CurseForge => ContentOriginProvider.CurseForge,
                CatalogProvider.Modrinth => ContentOriginProvider.Modrinth,
                _ => ContentOriginProvider.Unknown
            },
            ContentType = ToSourceContentType(file.ContentType),
            ProjectId = file.ProjectId,
            FileId = file.FileId,
            OriginalUrl = file.FilePageUrl ?? file.ProjectUrl,
            AcquiredAtUtc = DateTimeOffset.UtcNow
        };
    }

    internal static string ResolveInstalledRelativePath(CatalogContentType contentType, string fileName)
    {
        var normalizedFileName = string.IsNullOrWhiteSpace(fileName) ? "content.bin" : fileName.Trim();
        var directory = contentType switch
        {
            CatalogContentType.Mod => "mods",
            CatalogContentType.ResourcePack => "resourcepacks",
            CatalogContentType.Shader => "shaderpacks",
            _ => throw new InvalidOperationException($"Unsupported content type '{contentType}'.")
        };

        return NormalizeRelativePath(Path.Combine(directory, normalizedFileName));
    }

    internal static string? GetLoaderText(LoaderType loaderType)
    {
        return loaderType switch
        {
            LoaderType.Forge => "forge",
            LoaderType.NeoForge => "neoforge",
            LoaderType.Fabric => "fabric",
            LoaderType.Quilt => "quilt",
            _ => null
        };
    }

    internal static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory, bool overwrite)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        Directory.CreateDirectory(destinationDirectory);

        foreach (var directoryPath in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directoryPath);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var filePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            var destinationParent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationParent))
            {
                Directory.CreateDirectory(destinationParent);
            }

            File.Copy(filePath, destinationPath, overwrite);
        }
    }

    internal static string ResolveDownloadsDirectory(string? downloadsDirectory)
    {
        if (!string.IsNullOrWhiteSpace(downloadsDirectory))
        {
            return Path.GetFullPath(downloadsDirectory.Trim());
        }

        var profileDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profileDirectory))
        {
            return Path.GetFullPath("Downloads");
        }

        return Path.Combine(profileDirectory, "Downloads");
    }

    internal static string? TryFindDownloadedFile(string downloadsDirectory, string fileName, long sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(downloadsDirectory) ||
            string.IsNullOrWhiteSpace(fileName) ||
            !Directory.Exists(downloadsDirectory))
        {
            return null;
        }

        var exactPath = Path.Combine(downloadsDirectory, fileName);
        if (IsMatchingDownloadedFile(exactPath, sizeBytes))
        {
            return exactPath;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        foreach (var candidatePath in Directory.EnumerateFiles(downloadsDirectory, $"*{extension}", SearchOption.TopDirectoryOnly))
        {
            var candidateName = Path.GetFileNameWithoutExtension(candidatePath);
            if (!candidateName.StartsWith(stem, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsMatchingDownloadedFile(candidatePath, sizeBytes))
            {
                return candidatePath;
            }
        }

        return null;
    }

    internal static string NormalizeRelativePath(string value)
    {
        return value.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string ToSourceContentType(CatalogContentType contentType)
    {
        return contentType switch
        {
            CatalogContentType.Mod => "mod",
            CatalogContentType.Modpack => "modpack",
            CatalogContentType.ResourcePack => "resourcepack",
            CatalogContentType.Shader => "shader",
            _ => "unknown"
        };
    }

    private static bool IsMatchingDownloadedFile(string candidatePath, long sizeBytes)
    {
        if (!File.Exists(candidatePath))
        {
            return false;
        }

        var extension = Path.GetExtension(candidatePath);
        if (string.Equals(extension, ".crdownload", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".part", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (sizeBytes <= 0)
        {
            return true;
        }

        try
        {
            var fileInfo = new FileInfo(candidatePath);
            return fileInfo.Length == sizeBytes;
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class CurseForgeModpackManifest
{
    [JsonPropertyName("minecraft")]
    public CurseForgeModpackMinecraft Minecraft { get; init; } = new();

    [JsonPropertyName("files")]
    public IReadOnlyList<CurseForgeModpackFileReference> Files { get; init; } = [];

    [JsonPropertyName("overrides")]
    public string? Overrides { get; init; }
}

internal sealed class CurseForgeModpackMinecraft
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("modLoaders")]
    public IReadOnlyList<CurseForgeModpackLoader> ModLoaders { get; init; } = [];
}

internal sealed class CurseForgeModpackLoader
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("primary")]
    public bool Primary { get; init; }
}

internal sealed class CurseForgeModpackFileReference
{
    [JsonPropertyName("projectID")]
    public long ProjectId { get; init; }

    [JsonPropertyName("fileID")]
    public long FileId { get; init; }
}

internal sealed class ModrinthModpackManifest
{
    [JsonPropertyName("files")]
    public IReadOnlyList<ModrinthModpackFileReference> Files { get; init; } = [];

    [JsonPropertyName("dependencies")]
    public ModrinthModpackDependencies Dependencies { get; init; } = new();
}

internal sealed class ModrinthModpackDependencies
{
    [JsonPropertyName("minecraft")]
    public string Minecraft { get; init; } = string.Empty;

    [JsonPropertyName("fabric-loader")]
    public string? FabricLoader { get; init; }

    [JsonPropertyName("quilt-loader")]
    public string? QuiltLoader { get; init; }

    [JsonPropertyName("forge")]
    public string? Forge { get; init; }

    [JsonPropertyName("neoforge")]
    public string? NeoForge { get; init; }
}

internal sealed class ModrinthModpackFileReference
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("downloads")]
    public IReadOnlyList<string> Downloads { get; init; } = [];

    [JsonPropertyName("hashes")]
    public ModrinthModpackHashes Hashes { get; init; } = new();

    [JsonPropertyName("project_id")]
    public string? ProjectId { get; init; }

    [JsonPropertyName("file_id")]
    public string? FileId { get; init; }
}

internal sealed class ModrinthModpackHashes
{
    [JsonPropertyName("sha1")]
    public string? Sha1 { get; init; }
}

internal sealed class CatalogModpackImportContext
{
    public LauncherInstance Instance { get; init; } = default!;
    public string InstalledPath { get; init; } = string.Empty;
    public InstanceContentMetadata Metadata { get; init; } = default!;
    public IReadOnlyList<PendingManualDownloadFile> PendingManualDownloads { get; init; } = [];
}
