using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Infrastructure.Downloads;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Catalog;

public sealed class ImportArchiveInstanceRequest
{
    public string ArchivePath { get; init; } = string.Empty;
    public string InstanceName { get; init; } = string.Empty;
    public string? TargetDirectory { get; init; }
    public bool OverwriteIfExists { get; init; }
    public bool DownloadRuntime { get; init; }
    public string? DownloadsDirectory { get; init; }
    public bool WaitForManualDownloads { get; init; }
    public TimeSpan WaitTimeout { get; init; } = TimeSpan.FromMinutes(30);
}

public sealed class ImportArchiveInstanceResult
{
    public LauncherInstance Instance { get; init; } = default!;
    public string InstalledPath { get; init; } = string.Empty;
    public InstanceContentMetadata Metadata { get; init; } = default!;
    public string DownloadsDirectory { get; init; } = string.Empty;
    public IReadOnlyList<PendingManualDownloadFile> PendingManualDownloads { get; init; } = [];
    public bool IsCompleted => PendingManualDownloads.Count == 0;
}

public sealed class ImportArchiveInstanceUseCase
{
    private readonly ITempWorkspaceFactory tempWorkspaceFactory;
    private readonly IArchiveExtractor archiveExtractor;
    private readonly ImportInstanceUseCase importInstanceUseCase;
    private readonly IContentCatalogFileService contentCatalogFileService;
    private readonly IDownloader downloader;
    private readonly InstallInstanceUseCase installInstanceUseCase;
    private readonly IInstanceContentMetadataService instanceContentMetadataService;
    private readonly IManualDownloadStateStore manualDownloadStateStore;

    public ImportArchiveInstanceUseCase(
        ITempWorkspaceFactory tempWorkspaceFactory,
        IArchiveExtractor archiveExtractor,
        ImportInstanceUseCase importInstanceUseCase,
        IContentCatalogFileService contentCatalogFileService,
        IDownloader downloader,
        InstallInstanceUseCase installInstanceUseCase,
        IInstanceContentMetadataService instanceContentMetadataService,
        IManualDownloadStateStore manualDownloadStateStore)
    {
        this.tempWorkspaceFactory = tempWorkspaceFactory ?? throw new ArgumentNullException(nameof(tempWorkspaceFactory));
        this.archiveExtractor = archiveExtractor ?? throw new ArgumentNullException(nameof(archiveExtractor));
        this.importInstanceUseCase = importInstanceUseCase ?? throw new ArgumentNullException(nameof(importInstanceUseCase));
        this.contentCatalogFileService = contentCatalogFileService ?? throw new ArgumentNullException(nameof(contentCatalogFileService));
        this.downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        this.installInstanceUseCase = installInstanceUseCase ?? throw new ArgumentNullException(nameof(installInstanceUseCase));
        this.instanceContentMetadataService = instanceContentMetadataService ?? throw new ArgumentNullException(nameof(instanceContentMetadataService));
        this.manualDownloadStateStore = manualDownloadStateStore ?? throw new ArgumentNullException(nameof(manualDownloadStateStore));
    }

    public async Task<Result<ImportArchiveInstanceResult>> ExecuteAsync(
        ImportArchiveInstanceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.ArchivePath) ||
            string.IsNullOrWhiteSpace(request.InstanceName))
        {
            return Result<ImportArchiveInstanceResult>.Failure(CatalogFileErrors.InvalidRequest);
        }

        var archivePath = Path.GetFullPath(request.ArchivePath.Trim());
        if (!File.Exists(archivePath))
        {
            return Result<ImportArchiveInstanceResult>.Failure(InstallErrors.ImportSourceMissing);
        }

        await using var workspace = await tempWorkspaceFactory.CreateAsync("archive-import", cancellationToken).ConfigureAwait(false);
        var extractRoot = workspace.GetPath("extracted");
        var extractResult = await archiveExtractor.ExtractAsync(archivePath, extractRoot, cancellationToken).ConfigureAwait(false);
        if (extractResult.IsFailure)
        {
            return Result<ImportArchiveInstanceResult>.Failure(extractResult.Error);
        }

        if (File.Exists(Path.Combine(extractRoot, "manifest.json")))
        {
            return await ImportCurseForgeArchiveAsync(extractRoot, workspace, request, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(Path.Combine(extractRoot, "modrinth.index.json")))
        {
            return await ImportModrinthArchiveAsync(extractRoot, workspace, request, cancellationToken).ConfigureAwait(false);
        }

        var importRoot = ResolveImportRoot(extractRoot);
        if (importRoot is null)
        {
            return Result<ImportArchiveInstanceResult>.Failure(InstallErrors.ImportInvalidStructure);
        }

        var importResult = await importInstanceUseCase.ExecuteAsync(new ImportInstanceRequest
        {
            SourceDirectory = importRoot,
            InstanceName = request.InstanceName,
            TargetDirectory = request.TargetDirectory,
            CopyInsteadOfMove = true
        }, cancellationToken).ConfigureAwait(false);

        if (importResult.IsFailure)
        {
            return Result<ImportArchiveInstanceResult>.Failure(importResult.Error);
        }

        var metadata = await instanceContentMetadataService
            .GetAsync(importResult.Value.Instance, reindexIfMissing: true, cancellationToken)
            .ConfigureAwait(false) ?? new InstanceContentMetadata();

        return Result<ImportArchiveInstanceResult>.Success(new ImportArchiveInstanceResult
        {
            Instance = importResult.Value.Instance,
            InstalledPath = importResult.Value.InstalledPath,
            Metadata = metadata,
            DownloadsDirectory = CatalogInstallSupport.ResolveDownloadsDirectory(request.DownloadsDirectory),
            PendingManualDownloads = []
        });
    }

    private async Task<Result<ImportArchiveInstanceResult>> ImportCurseForgeArchiveAsync(
        string extractRoot,
        ITempWorkspace workspace,
        ImportArchiveInstanceRequest request,
        CancellationToken cancellationToken)
    {
        var manifestResult = await ImportCatalogModpackUseCase.ReadManifestAsync(extractRoot, cancellationToken).ConfigureAwait(false);
        if (manifestResult.IsFailure)
        {
            return Result<ImportArchiveInstanceResult>.Failure(manifestResult.Error);
        }

        if (!ImportCatalogModpackUseCase.TryResolveLoader(manifestResult.Value, out var loaderType, out var loaderVersion))
        {
            return Result<ImportArchiveInstanceResult>.Failure(CatalogFileErrors.ModpackLoaderUnsupported);
        }

        var installResult = await installInstanceUseCase.ExecuteAsync(new InstallInstanceRequest
        {
            InstanceName = request.InstanceName,
            GameVersion = manifestResult.Value.Minecraft.Version,
            LoaderType = loaderType,
            LoaderVersion = loaderVersion,
            TargetDirectory = request.TargetDirectory,
            OverwriteIfExists = request.OverwriteIfExists,
            DownloadRuntime = request.DownloadRuntime
        }, cancellationToken).ConfigureAwait(false);

        if (installResult.IsFailure)
        {
            return Result<ImportArchiveInstanceResult>.Failure(installResult.Error);
        }

        var stageRoot = workspace.GetPath("content-stage");
        Directory.CreateDirectory(stageRoot);

        var overridesRoot = string.IsNullOrWhiteSpace(manifestResult.Value.Overrides)
            ? Path.Combine(extractRoot, "overrides")
            : Path.Combine(extractRoot, manifestResult.Value.Overrides);
        if (Directory.Exists(overridesRoot))
        {
            CatalogInstallSupport.CopyDirectoryContents(overridesRoot, stageRoot, overwrite: true);
        }

        var sources = new Dictionary<string, ContentSourceMetadata>(StringComparer.OrdinalIgnoreCase);
        var pendingManualDownloads = new List<PendingManualDownloadFile>();
        foreach (var fileReference in manifestResult.Value.Files)
        {
            var fileResult = await contentCatalogFileService.ResolveFileAsync(new CatalogFileResolutionQuery
            {
                Provider = CatalogProvider.CurseForge,
                ContentType = CatalogContentType.Mod,
                ProjectId = fileReference.ProjectId.ToString(),
                FileId = fileReference.FileId.ToString()
            }, cancellationToken).ConfigureAwait(false);

            if (fileResult.IsFailure)
            {
                return Result<ImportArchiveInstanceResult>.Failure(fileResult.Error);
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
                return Result<ImportArchiveInstanceResult>.Failure(downloadResult.Error);
            }

            sources[relativePath] = CatalogInstallSupport.BuildSourceMetadata(file);
        }

        CatalogInstallSupport.CopyDirectoryContents(stageRoot, installResult.Value.InstalledPath, overwrite: true);
        var metadata = await instanceContentMetadataService
            .ApplySourcesAsync(installResult.Value.Instance, sources, cancellationToken)
            .ConfigureAwait(false);

        if (pendingManualDownloads.Count > 0)
        {
            await manualDownloadStateStore.SaveAsync(
                installResult.Value.Instance.InstallLocation,
                new PendingManualDownloadsState
                {
                    Provider = CatalogProvider.CurseForge,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Files = pendingManualDownloads
                },
                cancellationToken).ConfigureAwait(false);
        }

        return Result<ImportArchiveInstanceResult>.Success(new ImportArchiveInstanceResult
        {
            Instance = installResult.Value.Instance,
            InstalledPath = installResult.Value.InstalledPath,
            Metadata = metadata,
            DownloadsDirectory = CatalogInstallSupport.ResolveDownloadsDirectory(request.DownloadsDirectory),
            PendingManualDownloads = pendingManualDownloads
        });
    }

    private async Task<Result<ImportArchiveInstanceResult>> ImportModrinthArchiveAsync(
        string extractRoot,
        ITempWorkspace workspace,
        ImportArchiveInstanceRequest request,
        CancellationToken cancellationToken)
    {
        var manifestResult = await ImportCatalogModpackUseCase.ReadModrinthManifestAsync(extractRoot, cancellationToken).ConfigureAwait(false);
        if (manifestResult.IsFailure)
        {
            return Result<ImportArchiveInstanceResult>.Failure(manifestResult.Error);
        }

        if (!ImportCatalogModpackUseCase.TryResolveLoader(manifestResult.Value, out var loaderType, out var loaderVersion))
        {
            return Result<ImportArchiveInstanceResult>.Failure(CatalogFileErrors.ModpackLoaderUnsupported);
        }

        var installResult = await installInstanceUseCase.ExecuteAsync(new InstallInstanceRequest
        {
            InstanceName = request.InstanceName,
            GameVersion = manifestResult.Value.Dependencies.Minecraft,
            LoaderType = loaderType,
            LoaderVersion = loaderVersion,
            TargetDirectory = request.TargetDirectory,
            OverwriteIfExists = request.OverwriteIfExists,
            DownloadRuntime = request.DownloadRuntime
        }, cancellationToken).ConfigureAwait(false);

        if (installResult.IsFailure)
        {
            return Result<ImportArchiveInstanceResult>.Failure(installResult.Error);
        }

        var stageRoot = workspace.GetPath("content-stage");
        Directory.CreateDirectory(stageRoot);
        ImportCatalogModpackUseCase.CopyModrinthOverrideDirectories(extractRoot, stageRoot);

        var sources = new Dictionary<string, ContentSourceMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifestResult.Value.Files)
        {
            var downloadUrl = file.Downloads.FirstOrDefault(static item => Uri.TryCreate(item, UriKind.Absolute, out _));
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return Result<ImportArchiveInstanceResult>.Failure(CatalogFileErrors.DownloadUrlMissing);
            }

            var relativePath = CatalogInstallSupport.NormalizeRelativePath(file.Path);
            var stagedPath = workspace.GetPath(Path.Combine("content-stage", relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var downloadResult = await downloader.DownloadAsync(
                new DownloadRequest(new Uri(downloadUrl), stagedPath, file.Hashes.Sha1),
                cancellationToken).ConfigureAwait(false);
            if (downloadResult.IsFailure)
            {
                return Result<ImportArchiveInstanceResult>.Failure(downloadResult.Error);
            }

            var source = ImportCatalogModpackUseCase.BuildModrinthSourceMetadata(
                relativePath,
                CatalogProvider.Modrinth,
                file.ProjectId,
                file.FileId,
                downloadUrl);
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

        return Result<ImportArchiveInstanceResult>.Success(new ImportArchiveInstanceResult
        {
            Instance = installResult.Value.Instance,
            InstalledPath = installResult.Value.InstalledPath,
            Metadata = metadata,
            DownloadsDirectory = CatalogInstallSupport.ResolveDownloadsDirectory(request.DownloadsDirectory),
            PendingManualDownloads = []
        });
    }

    private static string? ResolveImportRoot(string extractedRoot)
    {
        if (LooksLikeInstanceDirectory(extractedRoot))
        {
            return extractedRoot;
        }

        var childDirectories = Directory.GetDirectories(extractedRoot, "*", SearchOption.TopDirectoryOnly);
        if (childDirectories.Length == 1 && LooksLikeInstanceDirectory(childDirectories[0]))
        {
            return childDirectories[0];
        }

        return null;
    }

    private static bool LooksLikeInstanceDirectory(string sourceDirectory)
    {
        return Directory.Exists(Path.Combine(sourceDirectory, ".minecraft")) ||
               File.Exists(Path.Combine(sourceDirectory, "instance.json")) ||
               Directory.Exists(Path.Combine(sourceDirectory, "mods")) ||
               Directory.Exists(Path.Combine(sourceDirectory, "config"));
    }
}
