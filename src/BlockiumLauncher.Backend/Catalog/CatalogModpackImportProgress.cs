namespace BlockiumLauncher.Application.UseCases.Catalog;

public enum ModpackImportPhase
{
    ResolvingModpack = 0,
    DownloadingArchive = 1,
    ExtractingArchive = 2,
    CheckingCurseForgeFiles = 3,
    WaitingForBlockedFilesDecision = 4,
    PreparingInstanceRuntime = 5,
    DownloadingAllowedFiles = 6,
    CopyingOverrides = 7,
    Finalizing = 8,
    Completed = 9,
    SkippedManual = 10,
    Failed = 11,
    Canceled = 12
}

public sealed class ModpackImportProgress
{
    public ModpackImportPhase Phase { get; init; }
    public string Title { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public int? CurrentFileCount { get; init; }
    public int? TotalFileCount { get; init; }
    public long? CurrentBytes { get; init; }
    public long? TotalBytes { get; init; }
    public string? CurrentItem { get; init; }
    public int? BlockedFileCount { get; init; }
    public int? ResolvedBlockedFileCount { get; init; }
}
