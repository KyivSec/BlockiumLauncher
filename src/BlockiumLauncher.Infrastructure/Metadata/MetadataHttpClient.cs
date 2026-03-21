using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Metadata;

public sealed class MetadataHttpClient : IMetadataHttpClient
{
    private readonly HttpClient HttpClient;
    private readonly MetadataHttpOptions Options;

    public MetadataHttpClient(HttpClient HttpClient, MetadataHttpOptions Options)
    {
        this.HttpClient = HttpClient;
        this.Options = Options;
        this.HttpClient.Timeout = Options.Timeout;
    }

    public async Task<Result<string>> GetStringAsync(Uri Uri, CancellationToken CancellationToken)
    {
        for (var Attempt = 1; Attempt <= Options.MaxAttempts; Attempt++) {
            try {
                using var Response = await HttpClient.GetAsync(
                    Uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    CancellationToken);

                if (Response.IsSuccessStatusCode) {
                    var Content = await Response.Content.ReadAsStringAsync(CancellationToken);
                    return Result<string>.Success(Content);
                }

                if (Attempt < Options.MaxAttempts && RetryPolicy.ShouldRetry(Response.StatusCode)) {
                    await Task.Delay(RetryPolicy.GetDelay(Attempt, Options), CancellationToken);
                    continue;
                }

                return Result<string>.Failure(
                    MetadataErrors.HttpFailed(
                        $"HTTP request failed with status code {(int)Response.StatusCode}.",
                        Uri.ToString()));
            }
            catch (Exception Exception) when (RetryPolicy.IsTransient(Exception, CancellationToken) && Attempt < Options.MaxAttempts) {
                await Task.Delay(RetryPolicy.GetDelay(Attempt, Options), CancellationToken);
            }
            catch (TaskCanceledException Exception) when (!CancellationToken.IsCancellationRequested) {
                return Result<string>.Failure(
                    MetadataErrors.Timeout(
                        "HTTP request timed out.",
                        Exception.Message));
            }
            catch (Exception Exception) {
                return Result<string>.Failure(
                    MetadataErrors.HttpFailed(
                        "HTTP request failed.",
                        Exception.Message));
            }
        }

        return Result<string>.Failure(
            MetadataErrors.HttpFailed("HTTP request failed after all retry attempts."));
    }

    public async Task<Result<Stream>> GetStreamAsync(Uri Uri, CancellationToken CancellationToken)
    {
        for (var Attempt = 1; Attempt <= Options.MaxAttempts; Attempt++) {
            try {
                var Response = await HttpClient.GetAsync(
                    Uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    CancellationToken);

                if (Response.IsSuccessStatusCode) {
                    var Stream = await Response.Content.ReadAsStreamAsync(CancellationToken);
                    return Result<Stream>.Success(new ResponseStream(Stream, Response));
                }

                Response.Dispose();

                if (Attempt < Options.MaxAttempts && RetryPolicy.ShouldRetry(Response.StatusCode)) {
                    await Task.Delay(RetryPolicy.GetDelay(Attempt, Options), CancellationToken);
                    continue;
                }

                return Result<Stream>.Failure(
                    MetadataErrors.HttpFailed(
                        $"HTTP request failed with status code {(int)Response.StatusCode}.",
                        Uri.ToString()));
            }
            catch (Exception Exception) when (RetryPolicy.IsTransient(Exception, CancellationToken) && Attempt < Options.MaxAttempts) {
                await Task.Delay(RetryPolicy.GetDelay(Attempt, Options), CancellationToken);
            }
            catch (TaskCanceledException Exception) when (!CancellationToken.IsCancellationRequested) {
                return Result<Stream>.Failure(
                    MetadataErrors.Timeout(
                        "HTTP request timed out.",
                        Exception.Message));
            }
            catch (Exception Exception) {
                return Result<Stream>.Failure(
                    MetadataErrors.HttpFailed(
                        "HTTP request failed.",
                        Exception.Message));
            }
        }

        return Result<Stream>.Failure(
            MetadataErrors.HttpFailed("HTTP request failed after all retry attempts."));
    }

    private sealed class ResponseStream : Stream
    {
        private readonly Stream InnerStream;
        private readonly HttpResponseMessage Response;

        public ResponseStream(Stream InnerStream, HttpResponseMessage Response)
        {
            this.InnerStream = InnerStream;
            this.Response = Response;
        }

        public override bool CanRead => InnerStream.CanRead;
        public override bool CanSeek => InnerStream.CanSeek;
        public override bool CanWrite => InnerStream.CanWrite;
        public override long Length => InnerStream.Length;

        public override long Position
        {
            get => InnerStream.Position;
            set => InnerStream.Position = value;
        }

        public override void Flush()
        {
            InnerStream.Flush();
        }

        public override int Read(byte[] Buffer, int Offset, int Count)
        {
            return InnerStream.Read(Buffer, Offset, Count);
        }

        public override long Seek(long Offset, SeekOrigin Origin)
        {
            return InnerStream.Seek(Offset, Origin);
        }

        public override void SetLength(long Value)
        {
            InnerStream.SetLength(Value);
        }

        public override void Write(byte[] Buffer, int Offset, int Count)
        {
            InnerStream.Write(Buffer, Offset, Count);
        }

        protected override void Dispose(bool Disposing)
        {
            if (Disposing) {
                InnerStream.Dispose();
                Response.Dispose();
            }

            base.Dispose(Disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await InnerStream.DisposeAsync();
            Response.Dispose();
            await base.DisposeAsync();
        }

        public override Task FlushAsync(CancellationToken CancellationToken)
        {
            return InnerStream.FlushAsync(CancellationToken);
        }

        public override Task<int> ReadAsync(byte[] Buffer, int Offset, int Count, CancellationToken CancellationToken)
        {
            return InnerStream.ReadAsync(Buffer, Offset, Count, CancellationToken);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> Buffer, CancellationToken CancellationToken = default)
        {
            return InnerStream.ReadAsync(Buffer, CancellationToken);
        }

        public override Task WriteAsync(byte[] Buffer, int Offset, int Count, CancellationToken CancellationToken)
        {
            return InnerStream.WriteAsync(Buffer, Offset, Count, CancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> Buffer, CancellationToken CancellationToken = default)
        {
            return InnerStream.WriteAsync(Buffer, CancellationToken);
        }
    }
}




