using System.Security.Cryptography;
using BlockiumLauncher.Infrastructure.Metadata;
using BlockiumLauncher.Shared.Errors;
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

    public sealed record DownloadBatchRequest(
        IReadOnlyList<DownloadRequest> Requests,
        int MaxConcurrency = 8);

    public sealed record DownloadBatchResult(
        IReadOnlyList<DownloadResult> Downloads,
        long BytesWritten);

    public sealed record DownloadBatchProgress(
        int CompletedFiles,
        int TotalFiles,
        long BytesWritten,
        long? TotalBytes,
        string? CurrentItem);

    public interface IDownloader
    {
        Task<Result<DownloadResult>> DownloadAsync(DownloadRequest Request, CancellationToken CancellationToken);
        Task<Result<DownloadBatchResult>> DownloadBatchAsync(
            DownloadBatchRequest Request,
            IProgress<DownloadBatchProgress>? Progress = null,
            CancellationToken CancellationToken = default);
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
            return await DownloadSingleAsync(Request, null, CancellationToken).ConfigureAwait(false);
        }

        public async Task<Result<DownloadBatchResult>> DownloadBatchAsync(
            DownloadBatchRequest Request,
            IProgress<DownloadBatchProgress>? Progress = null,
            CancellationToken CancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(Request);

            if (Request.Requests.Count == 0)
            {
                return Result<DownloadBatchResult>.Success(new DownloadBatchResult([], 0));
            }

            var results = new DownloadResult[Request.Requests.Count];
            var gate = new SemaphoreSlim(Math.Max(1, Request.MaxConcurrency));
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
            var failureSync = new object();

            Error? failure = null;
            long bytesWritten = 0;
            long knownTotalBytes = 0;
            int completedFiles = 0;
            int startedFiles = 0;
            int unknownLengthFiles = 0;

            void ReportProgress(string? currentItem)
            {
                long? totalBytes = Volatile.Read(ref startedFiles) == Request.Requests.Count && Volatile.Read(ref unknownLengthFiles) == 0
                    ? Volatile.Read(ref knownTotalBytes)
                    : null;

                Progress?.Report(new DownloadBatchProgress(
                    Volatile.Read(ref completedFiles),
                    Request.Requests.Count,
                    Volatile.Read(ref bytesWritten),
                    totalBytes,
                    currentItem));
            }

            var tasks = Request.Requests
                .Select(async (downloadRequest, index) =>
                {
                    await gate.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
                    try
                    {
                        if (failure is not null)
                        {
                            return;
                        }

                        var downloadResult = await DownloadSingleAsync(
                            downloadRequest,
                            contentLength =>
                            {
                                Interlocked.Increment(ref startedFiles);
                                if (contentLength.HasValue)
                                {
                                    Interlocked.Add(ref knownTotalBytes, contentLength.Value);
                                }
                                else
                                {
                                    Interlocked.Increment(ref unknownLengthFiles);
                                }

                                ReportProgress(Path.GetFileName(downloadRequest.DestinationPath));
                            },
                            linkedCancellation.Token,
                            bytes => Interlocked.Add(ref bytesWritten, bytes),
                            currentBytes => ReportProgress(Path.GetFileName(downloadRequest.DestinationPath)))
                            .ConfigureAwait(false);

                        if (downloadResult.IsFailure)
                        {
                            var shouldCancel = false;
                            lock (failureSync)
                            {
                                if (failure is null)
                                {
                                    failure = downloadResult.Error;
                                    shouldCancel = true;
                                }
                            }

                            if (shouldCancel)
                            {
                                linkedCancellation.Cancel();
                            }

                            return;
                        }

                        results[index] = downloadResult.Value;
                        Interlocked.Increment(ref completedFiles);
                        ReportProgress(Path.GetFileName(downloadRequest.DestinationPath));
                    }
                    finally
                    {
                        gate.Release();
                    }
                })
                .ToArray();

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (failure is not null && !CancellationToken.IsCancellationRequested)
            {
            }

            if (failure is not null)
            {
                return Result<DownloadBatchResult>.Failure(failure);
            }

            return Result<DownloadBatchResult>.Success(new DownloadBatchResult(results, bytesWritten));
        }

        private async Task<Result<DownloadResult>> DownloadSingleAsync(
            DownloadRequest Request,
            Action<long?>? onStreamOpened,
            CancellationToken CancellationToken,
            Action<int>? onBytesWritten = null,
            Action<long>? onProgress = null)
        {
            var DirectoryPath = Path.GetDirectoryName(Request.DestinationPath);
            if (!string.IsNullOrWhiteSpace(DirectoryPath))
            {
                Directory.CreateDirectory(DirectoryPath);
            }

            var TempFilePath = Request.DestinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            var Response = await MetadataHttpClient.GetStreamResponseAsync(Request.Uri, CancellationToken).ConfigureAwait(false);
            if (Response.IsFailure)
            {
                return Result<DownloadResult>.Failure(Response.Error);
            }

            try
            {
                long bytesWritten = 0;
                onStreamOpened?.Invoke(Response.Value.ContentLength);

                await using (Response.Value.Stream.ConfigureAwait(false))
                await using (var OutputStream = File.Create(TempFilePath))
                {
                    var buffer = new byte[81920];
                    while (true)
                    {
                        var read = await Response.Value.Stream.ReadAsync(buffer.AsMemory(0, buffer.Length), CancellationToken).ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }

                        await OutputStream.WriteAsync(buffer.AsMemory(0, read), CancellationToken).ConfigureAwait(false);
                        bytesWritten += read;
                        onBytesWritten?.Invoke(read);
                        onProgress?.Invoke(bytesWritten);
                    }
                }

                if (!string.IsNullOrWhiteSpace(Request.Sha1))
                {
                    var ActualSha1 = await ComputeSha1Async(TempFilePath, CancellationToken).ConfigureAwait(false);

                    if (!string.Equals(ActualSha1, Request.Sha1, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(TempFilePath);

                        return Result<DownloadResult>.Failure(
                            MetadataErrors.HashMismatch(
                                "Downloaded file hash does not match expected SHA-1.",
                                $"Expected: {Request.Sha1}; Actual: {ActualSha1}"));
                    }
                }

                if (File.Exists(Request.DestinationPath))
                {
                    File.Delete(Request.DestinationPath);
                }

                File.Move(TempFilePath, Request.DestinationPath);

                return Result<DownloadResult>.Success(new DownloadResult(Request.DestinationPath, bytesWritten));
            }
            catch (Exception Exception)
            {
                if (File.Exists(TempFilePath))
                {
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
