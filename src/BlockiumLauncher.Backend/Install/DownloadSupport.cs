using System.Security.Cryptography;
using BlockiumLauncher.Infrastructure.Metadata;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Downloads
{
    public sealed record DownloadRequest(
        Uri Uri,
        string DestinationPath,
        string? Sha1 = null);

    public sealed record DownloadResult(
        string DestinationPath,
        long BytesWritten);

    public interface IDownloader
    {
        Task<Result<DownloadResult>> DownloadAsync(DownloadRequest Request, CancellationToken CancellationToken);
    }
}

namespace BlockiumLauncher.Infrastructure.Downloads
{
    public sealed class HttpDownloader : IDownloader
    {
        private readonly IMetadataHttpClient MetadataHttpClient;

        public HttpDownloader(IMetadataHttpClient MetadataHttpClient)
        {
            this.MetadataHttpClient = MetadataHttpClient;
        }

        public async Task<Result<DownloadResult>> DownloadAsync(DownloadRequest Request, CancellationToken CancellationToken)
        {
            var DirectoryPath = Path.GetDirectoryName(Request.DestinationPath);
            if (!string.IsNullOrWhiteSpace(DirectoryPath)) {
                Directory.CreateDirectory(DirectoryPath);
            }

            var TempFilePath = Request.DestinationPath + ".tmp";

            if (File.Exists(TempFilePath)) {
                File.Delete(TempFilePath);
            }

            var Response = await MetadataHttpClient.GetStreamAsync(Request.Uri, CancellationToken);
            if (Response.IsFailure) {
                return Result<DownloadResult>.Failure(Response.Error);
            }

            try {
                long BytesWritten;

                await using (Response.Value.ConfigureAwait(false))
                await using (var OutputStream = File.Create(TempFilePath)) {
                    await Response.Value.CopyToAsync(OutputStream, CancellationToken);
                    BytesWritten = OutputStream.Length;
                }

                if (!string.IsNullOrWhiteSpace(Request.Sha1)) {
                    var ActualSha1 = await ComputeSha1Async(TempFilePath, CancellationToken);

                    if (!string.Equals(ActualSha1, Request.Sha1, StringComparison.OrdinalIgnoreCase)) {
                        File.Delete(TempFilePath);

                        return Result<DownloadResult>.Failure(
                            MetadataErrors.HashMismatch(
                                "Downloaded file hash does not match expected SHA-1.",
                                $"Expected: {Request.Sha1}; Actual: {ActualSha1}"));
                    }
                }

                if (File.Exists(Request.DestinationPath)) {
                    File.Delete(Request.DestinationPath);
                }

                File.Move(TempFilePath, Request.DestinationPath);

                return Result<DownloadResult>.Success(new DownloadResult(Request.DestinationPath, BytesWritten));
            }
            catch (Exception Exception) {
                if (File.Exists(TempFilePath)) {
                    File.Delete(TempFilePath);
                }

                return Result<DownloadResult>.Failure(
                    MetadataErrors.HttpFailed(
                        "Failed to download file.",
                        Exception.Message));
            }
        }

        private static async Task<string> ComputeSha1Async(string FilePath, CancellationToken CancellationToken)
        {
            await using var Stream = File.OpenRead(FilePath);
            using var Sha1 = SHA1.Create();
            var Hash = await Sha1.ComputeHashAsync(Stream, CancellationToken);
            return Convert.ToHexString(Hash);
        }
    }
}
