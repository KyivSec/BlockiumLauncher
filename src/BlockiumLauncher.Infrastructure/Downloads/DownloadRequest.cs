namespace BlockiumLauncher.Infrastructure.Downloads;

public sealed record DownloadRequest(
    Uri Uri,
    string DestinationPath,
    string? Sha1 = null);
