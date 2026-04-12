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
    public IProgress<ModpackImportProgress>? Progress { get; init; }
    public Func<BlockedModpackFilesPromptRequest, CancellationToken, Task<BlockedModpackFilesPromptResult>>? BlockedFilesPromptAsync { get; init; }
}

public sealed class ImportArchiveInstanceResult
{
    public LauncherInstance Instance { get; init; } = default!;
    public string InstalledPath { get; init; } = string.Empty;
    public InstanceContentMetadata Metadata { get; init; } = default!;
    public string DownloadsDirectory { get; init; } = string.Empty;
    public IReadOnlyList<PendingManualDownloadFile> PendingManualDownloads { get; init; } = [];
    public bool WasManualDownloadStepSkipped { get; init; }
    public bool IsCompleted => PendingManualDownloads.Count == 0;
}

public sealed class ImportArchiveInstanceUseCase
{
    private readonly ITempWorkspaceFactory tempWorkspaceFactory;
    private readonly IArchiveExtractor archiveExtractor;
    private readonly ImportInstanceUseCase importInstanceUseCase;
    private readonly IContentCatalogFileService contentCatalogFileService;
    private readonly IDownloader downloader;
    private readonly CatalogModpackImportPipeline catalogModpackImportPipeline;
    private readonly IInstanceContentMetadataService instanceContentMetadataService;
    private readonly IManualDownloadStateStore manualDownloadStateStore;

    public ImportArchiveInstanceUseCase(
        ITempWorkspaceFactory tempWorkspaceFactory,
        IArchiveExtractor archiveExtractor,
        ImportInstanceUseCase importInstanceUseCase,
        IContentCatalogFileService contentCatalogFileService,
        IDownloader downloader,
        CatalogModpackImportPipeline catalogModpackImportPipeline,
        IInstanceContentMetadataService instanceContentMetadataService,
        IManualDownloadStateStore manualDownloadStateStore)
    {
        this.tempWorkspaceFactory = tempWorkspaceFactory ?? throw new ArgumentNullException(nameof(tempWorkspaceFactory));
        this.archiveExtractor = archiveExtractor ?? throw new ArgumentNullException(nameof(archiveExtractor));
        this.importInstanceUseCase = importInstanceUseCase ?? throw new ArgumentNullException(nameof(importInstanceUseCase));
        this.contentCatalogFileService = contentCatalogFileService ?? throw new ArgumentNullException(nameof(contentCatalogFileService));
        this.downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        this.catalogModpackImportPipeline = catalogModpackImportPipeline ?? throw new ArgumentNullException(nameof(catalogModpackImportPipeline));
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
            return await ImportCurseForgeArchiveAsync(extractRoot, request, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(Path.Combine(extractRoot, "modrinth.index.json")))
        {
            return await ImportModrinthArchiveAsync(extractRoot, request, cancellationToken).ConfigureAwait(false);
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
        ImportArchiveInstanceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await catalogModpackImportPipeline.ImportCurseForgeAsync(
            extractRoot,
            new ImportCatalogModpackRequest
            {
                Provider = CatalogProvider.CurseForge,
                InstanceName = request.InstanceName,
                TargetDirectory = request.TargetDirectory,
                OverwriteIfExists = request.OverwriteIfExists,
                DownloadRuntime = request.DownloadRuntime,
                DownloadsDirectory = request.DownloadsDirectory,
                Progress = request.Progress,
                BlockedFilesPromptAsync = request.BlockedFilesPromptAsync
            },
            CatalogInstallSupport.ResolveDownloadsDirectory(request.DownloadsDirectory),
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<ImportArchiveInstanceResult>.Failure(result.Error);
        }

        return Result<ImportArchiveInstanceResult>.Success(new ImportArchiveInstanceResult
        {
            Instance = result.Value.Instance,
            InstalledPath = result.Value.InstalledPath,
            Metadata = result.Value.Metadata,
            DownloadsDirectory = CatalogInstallSupport.ResolveDownloadsDirectory(request.DownloadsDirectory),
            PendingManualDownloads = [],
            WasManualDownloadStepSkipped = result.Value.WasManualDownloadStepSkipped
        });
    }

    private async Task<Result<ImportArchiveInstanceResult>> ImportModrinthArchiveAsync(
        string extractRoot,
        ImportArchiveInstanceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await catalogModpackImportPipeline.ImportModrinthAsync(
            extractRoot,
            new ImportCatalogModpackRequest
            {
                Provider = CatalogProvider.Modrinth,
                InstanceName = request.InstanceName,
                TargetDirectory = request.TargetDirectory,
                OverwriteIfExists = request.OverwriteIfExists,
                DownloadRuntime = request.DownloadRuntime,
                Progress = request.Progress
            },
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<ImportArchiveInstanceResult>.Failure(result.Error);
        }

        return Result<ImportArchiveInstanceResult>.Success(new ImportArchiveInstanceResult
        {
            Instance = result.Value.Instance,
            InstalledPath = result.Value.InstalledPath,
            Metadata = result.Value.Metadata,
            DownloadsDirectory = CatalogInstallSupport.ResolveDownloadsDirectory(request.DownloadsDirectory),
            PendingManualDownloads = [],
            WasManualDownloadStepSkipped = false
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
