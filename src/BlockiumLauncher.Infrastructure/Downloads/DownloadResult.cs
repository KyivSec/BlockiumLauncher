namespace BlockiumLauncher.Infrastructure.Downloads;

public sealed record DownloadResult(
    string DestinationPath,
    long BytesWritten);
