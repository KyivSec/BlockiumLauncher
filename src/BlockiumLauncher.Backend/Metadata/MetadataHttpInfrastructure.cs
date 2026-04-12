using System.Net;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Metadata;

public interface IMetadataHttpClient
{
    Task<Result<string>> GetStringAsync(Uri Uri, CancellationToken CancellationToken);
    Task<Result<Stream>> GetStreamAsync(Uri Uri, CancellationToken CancellationToken);
    Task<Result<MetadataStreamResponse>> GetStreamResponseAsync(Uri Uri, CancellationToken CancellationToken);
}

public sealed record MetadataStreamResponse(
    Stream Stream,
    long? ContentLength);

public sealed class MetadataCachePolicy
{
    public TimeSpan FreshTtl { get; init; } = TimeSpan.FromHours(6);
    public TimeSpan MaxStaleFallbackAge { get; init; } = TimeSpan.FromDays(7);

    public bool IsFresh(DateTimeOffset LastUpdatedUtc, DateTimeOffset NowUtc)
    {
        return NowUtc - LastUpdatedUtc <= FreshTtl;
    }

    public bool CanUseStaleFallback(DateTimeOffset LastUpdatedUtc, DateTimeOffset NowUtc)
    {
        return NowUtc - LastUpdatedUtc <= MaxStaleFallbackAge;
    }
}

internal static class MetadataEndpoints
{
    internal const string VanillaVersionManifest = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";

    internal static string FabricLoaderVersions(string GameVersion) => $"https://meta.fabricmc.net/v2/versions/loader/{GameVersion}";
    internal static string QuiltLoaderVersions(string GameVersion) => $"https://meta.quiltmc.org/v3/versions/loader/{GameVersion}";

    internal const string ForgeMavenMetadata = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
    internal const string NeoForgeMavenMetadata = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
    internal const string ModrinthSearch = "https://api.modrinth.com/v2/search";
    internal const string ModrinthCategories = "https://api.modrinth.com/v2/tag/category";
    internal const string ModrinthLoaders = "https://api.modrinth.com/v2/tag/loader";
    internal const string ModrinthGameVersions = "https://api.modrinth.com/v2/tag/game_version";
    internal static string ModrinthProject(string projectId) => $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}";
    internal static string ModrinthProjectVersions(string projectId) => $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}/version";
    internal static string ModrinthVersion(string versionId) => $"https://api.modrinth.com/v2/version/{Uri.EscapeDataString(versionId)}";
    internal const string CurseForgeCategories = "https://api.curseforge.com/v1/categories";
    internal const string CurseForgeModsSearch = "https://api.curseforge.com/v1/mods/search";
    internal const string CurseForgeMods = "https://api.curseforge.com/v1/mods";
    internal const string CurseForgeFiles = "https://api.curseforge.com/v1/mods/files";
    internal static string CurseForgeMod(string modId) => $"https://api.curseforge.com/v1/mods/{modId}";
    internal static string CurseForgeModDescription(string modId) => $"https://api.curseforge.com/v1/mods/{modId}/description";
    internal static string CurseForgeModFiles(string modId) => $"https://api.curseforge.com/v1/mods/{modId}/files";
    internal static string CurseForgeModFile(string modId, string fileId) => $"https://api.curseforge.com/v1/mods/{modId}/files/{fileId}";
    internal static string CurseForgeModFileChangelog(string modId, string fileId) => $"https://api.curseforge.com/v1/mods/{modId}/files/{fileId}/changelog";
    internal static string CurseForgeModFileDownloadUrl(string modId, string fileId) => $"https://api.curseforge.com/v1/mods/{modId}/files/{fileId}/download-url";
}

internal static class MetadataErrors
{
    internal static Error HttpFailed(string Message, string? Details = null) => new("Metadata.HttpFailed", Message, Details);
    internal static Error Timeout(string Message, string? Details = null) => new("Metadata.Timeout", Message, Details);
    internal static Error InvalidPayload(string Message, string? Details = null) => new("Metadata.InvalidPayload", Message, Details);
    internal static Error UnsupportedLoaderType(string Message, string? Details = null) => new("Loader.UnsupportedType", Message, Details);
    internal static Error NotFound(string Message, string? Details = null) => new("Metadata.NotFound", Message, Details);
    internal static Error HashMismatch(string Message, string? Details = null) => new("Download.HashMismatch", Message, Details);
}

public sealed class MetadataHttpOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxAttempts { get; init; } = 3;
    public TimeSpan FirstRetryDelay { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan SecondRetryDelay { get; init; } = TimeSpan.FromMilliseconds(750);
    public TimeSpan ThirdRetryDelay { get; init; } = TimeSpan.FromMilliseconds(1500);
}

internal static class RetryPolicy
{
    internal static bool ShouldRetry(HttpStatusCode StatusCode)
    {
        var NumericStatusCode = (int)StatusCode;

        return StatusCode == HttpStatusCode.RequestTimeout
            || NumericStatusCode == 429
            || NumericStatusCode >= 500;
    }

    internal static bool IsTransient(Exception Exception, CancellationToken CancellationToken)
    {
        if (CancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return Exception is HttpRequestException || Exception is TaskCanceledException;
    }

    internal static TimeSpan GetDelay(int Attempt, MetadataHttpOptions Options)
    {
        return Attempt switch
        {
            1 => Options.FirstRetryDelay,
            2 => Options.SecondRetryDelay,
            _ => Options.ThirdRetryDelay
        };
    }
}

public sealed class MetadataHttpClient : IMetadataHttpClient
{
    private readonly HttpClient HttpClient;
    private readonly MetadataHttpOptions Options;

    public MetadataHttpClient(HttpClient HttpClient, MetadataHttpOptions Options)
    {
        this.HttpClient = HttpClient;
        this.Options = Options;
        this.HttpClient.Timeout = Options.Timeout;
        if (!this.HttpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            this.HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BlockiumLauncher/0.1");
        }
    }

    public async Task<Result<string>> GetStringAsync(Uri Uri, CancellationToken CancellationToken)
    {
        for (var Attempt = 1; Attempt <= Options.MaxAttempts; Attempt++)
        {
            try
            {
                using var Response = await HttpClient.GetAsync(
                    Uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    CancellationToken);

                if (Response.IsSuccessStatusCode)
                {
                    var Content = await Response.Content.ReadAsStringAsync(CancellationToken);
                    return Result<string>.Success(Content);
                }

                if (Attempt < Options.MaxAttempts && RetryPolicy.ShouldRetry(Response.StatusCode))
                {
                    await Task.Delay(RetryPolicy.GetDelay(Attempt, Options), CancellationToken);
                    continue;
                }

                return Result<string>.Failure(
                    MetadataErrors.HttpFailed(
                        $"HTTP request failed with status code {(int)Response.StatusCode}.",
                        Uri.ToString()));
            }
            catch (Exception Exception) when (RetryPolicy.IsTransient(Exception, CancellationToken) && Attempt < Options.MaxAttempts)
            {
                await Task.Delay(RetryPolicy.GetDelay(Attempt, Options), CancellationToken);
            }
            catch (TaskCanceledException Exception) when (!CancellationToken.IsCancellationRequested)
            {
                return Result<string>.Failure(
                    MetadataErrors.Timeout(
                        "HTTP request timed out.",
                        Exception.Message));
            }
            catch (Exception Exception)
            {
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
        var responseResult = await GetStreamResponseAsync(Uri, CancellationToken).ConfigureAwait(false);
        return responseResult.IsFailure
            ? Result<Stream>.Failure(responseResult.Error)
            : Result<Stream>.Success(responseResult.Value.Stream);
    }

    public async Task<Result<MetadataStreamResponse>> GetStreamResponseAsync(Uri Uri, CancellationToken CancellationToken)
    {
        for (var Attempt = 1; Attempt <= Options.MaxAttempts; Attempt++)
        {
            try
            {
                var Response = await HttpClient.GetAsync(
                    Uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    CancellationToken);

                if (Response.IsSuccessStatusCode)
                {
                    var Stream = await Response.Content.ReadAsStreamAsync(CancellationToken);
                    return Result<MetadataStreamResponse>.Success(new MetadataStreamResponse(
                        new ResponseStream(Stream, Response),
                        Response.Content.Headers.ContentLength));
                }

                Response.Dispose();

                if (Attempt < Options.MaxAttempts && RetryPolicy.ShouldRetry(Response.StatusCode))
                {
                    await Task.Delay(RetryPolicy.GetDelay(Attempt, Options), CancellationToken);
                    continue;
                }

                return Result<MetadataStreamResponse>.Failure(
                    MetadataErrors.HttpFailed(
                        $"HTTP request failed with status code {(int)Response.StatusCode}.",
                        Uri.ToString()));
            }
            catch (Exception Exception) when (RetryPolicy.IsTransient(Exception, CancellationToken) && Attempt < Options.MaxAttempts)
            {
                await Task.Delay(RetryPolicy.GetDelay(Attempt, Options), CancellationToken);
            }
            catch (TaskCanceledException Exception) when (!CancellationToken.IsCancellationRequested)
            {
                return Result<MetadataStreamResponse>.Failure(
                    MetadataErrors.Timeout(
                        "HTTP request timed out.",
                        Exception.Message));
            }
            catch (Exception Exception)
            {
                return Result<MetadataStreamResponse>.Failure(
                    MetadataErrors.HttpFailed(
                        "HTTP request failed.",
                        Exception.Message));
            }
        }

        return Result<MetadataStreamResponse>.Failure(
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

        public override void Flush() => InnerStream.Flush();
        public override int Read(byte[] Buffer, int Offset, int Count) => InnerStream.Read(Buffer, Offset, Count);
        public override long Seek(long Offset, SeekOrigin Origin) => InnerStream.Seek(Offset, Origin);
        public override void SetLength(long Value) => InnerStream.SetLength(Value);
        public override void Write(byte[] Buffer, int Offset, int Count) => InnerStream.Write(Buffer, Offset, Count);

        protected override void Dispose(bool Disposing)
        {
            if (Disposing)
            {
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

        public override Task FlushAsync(CancellationToken CancellationToken) => InnerStream.FlushAsync(CancellationToken);
        public override Task<int> ReadAsync(byte[] Buffer, int Offset, int Count, CancellationToken CancellationToken) => InnerStream.ReadAsync(Buffer, Offset, Count, CancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> Buffer, CancellationToken CancellationToken = default) => InnerStream.ReadAsync(Buffer, CancellationToken);
        public override Task WriteAsync(byte[] Buffer, int Offset, int Count, CancellationToken CancellationToken) => InnerStream.WriteAsync(Buffer, Offset, Count, CancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> Buffer, CancellationToken CancellationToken = default) => InnerStream.WriteAsync(Buffer, CancellationToken);
    }
}
