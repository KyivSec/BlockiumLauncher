using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Infrastructure.Downloads;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Catalog;

public sealed class CatalogModpackImportPipeline
{
    private readonly CurseForgeModpackPreflightService curseForgeModpackPreflightService;
    private readonly IContentCatalogFileService contentCatalogFileService;
    private readonly IDownloader downloader;
    private readonly InstallInstanceUseCase installInstanceUseCase;
    private readonly IInstanceContentMetadataService instanceContentMetadataService;
    private readonly IManualDownloadStateStore manualDownloadStateStore;

    public CatalogModpackImportPipeline(
        CurseForgeModpackPreflightService curseForgeModpackPreflightService,
        IContentCatalogFileService contentCatalogFileService,
        IDownloader downloader,
        InstallInstanceUseCase installInstanceUseCase,
        IInstanceContentMetadataService instanceContentMetadataService,
        IManualDownloadStateStore manualDownloadStateStore)
    {
        this.curseForgeModpackPreflightService = curseForgeModpackPreflightService ?? throw new ArgumentNullException(nameof(curseForgeModpackPreflightService));
        this.contentCatalogFileService = contentCatalogFileService ?? throw new ArgumentNullException(nameof(contentCatalogFileService));
        this.downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        this.installInstanceUseCase = installInstanceUseCase ?? throw new ArgumentNullException(nameof(installInstanceUseCase));
        this.instanceContentMetadataService = instanceContentMetadataService ?? throw new ArgumentNullException(nameof(instanceContentMetadataService));
        this.manualDownloadStateStore = manualDownloadStateStore ?? throw new ArgumentNullException(nameof(manualDownloadStateStore));
    }

    internal async Task<Result<CatalogModpackImportContext>> ImportCurseForgeAsync(
        string extractRoot,
        ImportCatalogModpackRequest request,
        string downloadsDirectory,
        CancellationToken cancellationToken)
    {
        var manifestResult = await ImportCatalogModpackUseCase.ReadManifestAsync(extractRoot, cancellationToken).ConfigureAwait(false);
        if (manifestResult.IsFailure)
        {
            return Result<CatalogModpackImportContext>.Failure(manifestResult.Error);
        }

        var manifest = manifestResult.Value;
        if (!ImportCatalogModpackUseCase.TryResolveLoader(manifest, out var loaderType, out var loaderVersion))
        {
            return Result<CatalogModpackImportContext>.Failure(CatalogFileErrors.ModpackLoaderUnsupported);
        }

        var requestedLoader = CatalogInstallSupport.GetLoaderText(loaderType);
        ReportProgress(request.Progress, new ModpackImportProgress
        {
            Phase = ModpackImportPhase.CheckingCurseForgeFiles,
            Title = "Checking CurseForge files",
            StatusText = $"Resolving {manifest.Files.Count} file(s) from the modpack manifest.",
            CurrentFileCount = 0,
            TotalFileCount = manifest.Files.Count
        });

        var preflightResult = await curseForgeModpackPreflightService.ExecuteAsync(manifest, requestedLoader, cancellationToken).ConfigureAwait(false);
        if (preflightResult.IsFailure)
        {
            return Result<CatalogModpackImportContext>.Failure(preflightResult.Error);
        }

        IReadOnlyList<PendingManualDownloadMatch> manualMatches = [];
        var wasManualStepSkipped = false;

        if (preflightResult.Value.BlockedFiles.Count > 0)
        {
            if (request.BlockedFilesPromptAsync is null)
            {
                return Result<CatalogModpackImportContext>.Failure(new Error(
                    "Catalog.BlockedFilesPromptMissing",
                    "Blocked CurseForge files require a manual-download prompt handler."));
            }

            ReportProgress(request.Progress, new ModpackImportProgress
            {
                Phase = ModpackImportPhase.WaitingForBlockedFilesDecision,
                Title = "Waiting for manual downloads",
                StatusText = $"Waiting for confirmation on {preflightResult.Value.BlockedFiles.Count} blocked file(s).",
                BlockedFileCount = preflightResult.Value.BlockedFiles.Count,
                ResolvedBlockedFileCount = 0
            });

            var promptResult = await request.BlockedFilesPromptAsync(new BlockedModpackFilesPromptRequest
            {
                Provider = CatalogProvider.CurseForge,
                DownloadsDirectory = downloadsDirectory,
                Files = preflightResult.Value.BlockedFiles
            }, cancellationToken).ConfigureAwait(false);

            manualMatches = promptResult.Matches;
            wasManualStepSkipped = promptResult.Decision == BlockedModpackFilesDecision.SkipMissing;
            if (promptResult.Decision == BlockedModpackFilesDecision.Cancel)
            {
                throw new OperationCanceledException("Blocked file import was cancelled.", cancellationToken);
            }
        }

        var prepareResult = await PrepareBaseInstanceAsync(
            request,
            manifest.Minecraft.Version,
            loaderType,
            loaderVersion,
            cancellationToken).ConfigureAwait(false);
        if (prepareResult.IsFailure)
        {
            return Result<CatalogModpackImportContext>.Failure(prepareResult.Error);
        }

        await using var preparedSession = prepareResult.Value;
        var sources = new Dictionary<string, ContentSourceMetadata>(StringComparer.OrdinalIgnoreCase);

        foreach (var match in manualMatches)
        {
            var destinationPath = Path.Combine(preparedSession.PreparedRootPath, match.File.DestinationRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(match.DownloadedPath, destinationPath, overwrite: true);
            sources[match.File.DestinationRelativePath] = CatalogInstallSupport.BuildSourceMetadata(match.File);
        }

        var downloadResult = await DownloadCatalogFilesAsync(
            request.Progress,
            preflightResult.Value.DownloadableFiles.Select(file => new CatalogDownloadableFile(
                file.File,
                file.DestinationRelativePath)).ToArray(),
            preparedSession.PreparedRootPath,
            cancellationToken).ConfigureAwait(false);
        if (downloadResult.IsFailure)
        {
            return Result<CatalogModpackImportContext>.Failure(downloadResult.Error);
        }

        foreach (var file in preflightResult.Value.DownloadableFiles)
        {
            sources[file.DestinationRelativePath] = CatalogInstallSupport.BuildSourceMetadata(file.File);
        }

        ReportProgress(request.Progress, new ModpackImportProgress
        {
            Phase = ModpackImportPhase.CopyingOverrides,
            Title = "Copying overrides",
            StatusText = "Applying modpack overrides to the staged instance."
        });

        var overridesRoot = string.IsNullOrWhiteSpace(manifest.Overrides)
            ? Path.Combine(extractRoot, "overrides")
            : Path.Combine(extractRoot, manifest.Overrides);
        if (Directory.Exists(overridesRoot))
        {
            CatalogInstallSupport.CopyDirectoryContents(overridesRoot, preparedSession.PreparedRootPath, overwrite: true);
        }

        return await CommitPreparedImportAsync(preparedSession, sources, wasManualStepSkipped, request.Progress, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<Result<CatalogModpackImportContext>> ImportModrinthAsync(
        string extractRoot,
        ImportCatalogModpackRequest request,
        CancellationToken cancellationToken)
    {
        var manifestResult = await ImportCatalogModpackUseCase.ReadModrinthManifestAsync(extractRoot, cancellationToken).ConfigureAwait(false);
        if (manifestResult.IsFailure)
        {
            return Result<CatalogModpackImportContext>.Failure(manifestResult.Error);
        }

        var manifest = manifestResult.Value;
        if (!ImportCatalogModpackUseCase.TryResolveLoader(manifest, out var loaderType, out var loaderVersion))
        {
            return Result<CatalogModpackImportContext>.Failure(CatalogFileErrors.ModpackLoaderUnsupported);
        }

        var prepareResult = await PrepareBaseInstanceAsync(
            request,
            manifest.Dependencies.Minecraft,
            loaderType,
            loaderVersion,
            cancellationToken).ConfigureAwait(false);
        if (prepareResult.IsFailure)
        {
            return Result<CatalogModpackImportContext>.Failure(prepareResult.Error);
        }

        await using var preparedSession = prepareResult.Value;
        var sources = new Dictionary<string, ContentSourceMetadata>(StringComparer.OrdinalIgnoreCase);
        var downloadables = new List<CatalogDownloadableFile>();

        foreach (var file in manifest.Files)
        {
            var downloadUrl = file.Downloads.FirstOrDefault(static item => Uri.TryCreate(item, UriKind.Absolute, out _));
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return Result<CatalogModpackImportContext>.Failure(CatalogFileErrors.DownloadUrlMissing);
            }

            var relativePath = CatalogInstallSupport.NormalizeRelativePath(file.Path);
            downloadables.Add(new CatalogDownloadableFile(
                new CatalogFileSummary
                {
                    Provider = CatalogProvider.Modrinth,
                    ContentType = CatalogContentType.Mod,
                    ProjectId = file.ProjectId ?? string.Empty,
                    FileId = file.FileId ?? string.Empty,
                    DisplayName = file.ProjectId ?? file.Path,
                    FileName = Path.GetFileName(relativePath),
                    DownloadUrl = downloadUrl,
                    Sha1 = file.Hashes.Sha1
                },
                relativePath));

            var source = ImportCatalogModpackUseCase.BuildModrinthSourceMetadata(relativePath, request.Provider, file.ProjectId, file.FileId, downloadUrl);
            if (source is not null)
            {
                sources[relativePath] = source;
            }
        }

        var downloadResult = await DownloadCatalogFilesAsync(request.Progress, downloadables, preparedSession.PreparedRootPath, cancellationToken).ConfigureAwait(false);
        if (downloadResult.IsFailure)
        {
            return Result<CatalogModpackImportContext>.Failure(downloadResult.Error);
        }

        ReportProgress(request.Progress, new ModpackImportProgress
        {
            Phase = ModpackImportPhase.CopyingOverrides,
            Title = "Copying overrides",
            StatusText = "Applying modpack overrides to the staged instance."
        });
        ImportCatalogModpackUseCase.CopyModrinthOverrideDirectories(extractRoot, preparedSession.PreparedRootPath);

        return await CommitPreparedImportAsync(preparedSession, sources, wasManualDownloadStepSkipped: false, request.Progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<PreparedInstallSession>> PrepareBaseInstanceAsync(
        ImportCatalogModpackRequest request,
        string gameVersion,
        LoaderType loaderType,
        string? loaderVersion,
        CancellationToken cancellationToken)
    {
        var preparationProgress = new Progress<InstallPreparationProgress>(progress =>
            ReportProgress(request.Progress, MapInstallPreparationProgress(progress)));

        ReportProgress(request.Progress, new ModpackImportProgress
        {
            Phase = ModpackImportPhase.PreparingInstanceRuntime,
            Title = "Preparing instance runtime",
            StatusText = "Preparing the base runtime before downloading mod files."
        });

        return await installInstanceUseCase.PrepareAsync(new InstallInstanceRequest
        {
            InstanceName = request.InstanceName,
            GameVersion = gameVersion,
            LoaderType = loaderType,
            LoaderVersion = loaderVersion,
            TargetDirectory = request.TargetDirectory,
            OverwriteIfExists = request.OverwriteIfExists,
            DownloadRuntime = request.DownloadRuntime,
            PreparationProgress = preparationProgress
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<bool>> DownloadCatalogFilesAsync(
        IProgress<ModpackImportProgress>? progress,
        IReadOnlyList<CatalogDownloadableFile> files,
        string preparedRootPath,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            return Result<bool>.Success(true);
        }

        var batchRequests = files
            .Select(file => new DownloadRequest(
                new Uri(file.File.DownloadUrl!),
                Path.Combine(preparedRootPath, file.DestinationRelativePath.Replace('/', Path.DirectorySeparatorChar)),
                file.File.Sha1))
            .ToArray();

        var batchProgress = new Progress<DownloadBatchProgress>(update =>
        {
            ReportProgress(progress, new ModpackImportProgress
            {
                Phase = ModpackImportPhase.DownloadingAllowedFiles,
                Title = "Downloading mod files",
                StatusText = $"Downloaded {update.CompletedFiles} of {update.TotalFiles} file(s).",
                CurrentFileCount = update.CompletedFiles,
                TotalFileCount = update.TotalFiles,
                CurrentBytes = update.BytesWritten,
                TotalBytes = update.TotalBytes,
                CurrentItem = update.CurrentItem
            });
        });

        var batchResult = await downloader.DownloadBatchAsync(
            new DownloadBatchRequest(batchRequests, MaxConcurrency: 8),
            batchProgress,
            cancellationToken).ConfigureAwait(false);
        if (batchResult.IsFailure)
        {
            return Result<bool>.Failure(batchResult.Error);
        }

        return Result<bool>.Success(true);
    }

    private async Task<Result<CatalogModpackImportContext>> CommitPreparedImportAsync(
        PreparedInstallSession preparedSession,
        IReadOnlyDictionary<string, ContentSourceMetadata> sources,
        bool wasManualDownloadStepSkipped,
        IProgress<ModpackImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        ReportProgress(progress, new ModpackImportProgress
        {
            Phase = ModpackImportPhase.Finalizing,
            Title = "Finalizing import",
            StatusText = "Committing the staged instance to launcher storage."
        });

        var commitResult = await installInstanceUseCase.CommitAsync(preparedSession, cancellationToken).ConfigureAwait(false);
        if (commitResult.IsFailure)
        {
            return Result<CatalogModpackImportContext>.Failure(commitResult.Error);
        }

        var metadata = await instanceContentMetadataService
            .ApplySourcesAsync(commitResult.Value.Instance, sources, cancellationToken)
            .ConfigureAwait(false);

        await manualDownloadStateStore.DeleteAsync(commitResult.Value.Instance.InstallLocation, cancellationToken).ConfigureAwait(false);

        return Result<CatalogModpackImportContext>.Success(new CatalogModpackImportContext
        {
            Instance = commitResult.Value.Instance,
            InstalledPath = commitResult.Value.InstalledPath,
            Metadata = metadata,
            PendingManualDownloads = [],
            WasManualDownloadStepSkipped = wasManualDownloadStepSkipped
        });
    }

    private static ModpackImportProgress MapInstallPreparationProgress(InstallPreparationProgress progress)
    {
        return new ModpackImportProgress
        {
            Phase = ModpackImportPhase.PreparingInstanceRuntime,
            Title = progress.Title,
            StatusText = progress.StatusText,
            CurrentFileCount = progress.Current,
            TotalFileCount = progress.Total
        };
    }

    private static void ReportProgress(IProgress<ModpackImportProgress>? progress, ModpackImportProgress update)
    {
        progress?.Report(update);
    }

    private sealed record CatalogDownloadableFile(
        CatalogFileSummary File,
        string DestinationRelativePath);
}
