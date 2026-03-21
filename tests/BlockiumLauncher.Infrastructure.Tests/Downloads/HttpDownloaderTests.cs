using System.Net;
using System.Security.Cryptography;
using BlockiumLauncher.Infrastructure.Downloads;
using BlockiumLauncher.Infrastructure.Metadata;
using BlockiumLauncher.Infrastructure.Tests.Metadata;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Downloads;

public sealed class HttpDownloaderTests
{
    [Fact]
    public async Task DownloadAsync_WritesFile()
    {
        var Content = "hello world";
        var Bytes = System.Text.Encoding.UTF8.GetBytes(Content);

        var Handler = new FakeHttpMessageHandler((Request, CancellationToken) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Bytes)
            });
        });

        var HttpClient = new HttpClient(Handler);
        var MetadataHttpClient = new MetadataHttpClient(HttpClient, new MetadataHttpOptions());
        var Downloader = new HttpDownloader(MetadataHttpClient);

        var RootDirectory = Path.Combine(Path.GetTempPath(), "BlockiumLauncherTests", Guid.NewGuid().ToString("N"));
        var FilePath = Path.Combine(RootDirectory, "downloads", "sample.bin");

        var Result = await Downloader.DownloadAsync(
            new DownloadRequest(new Uri("https://example.invalid/file"), FilePath),
            CancellationToken.None);

        Assert.True(Result.IsSuccess);
        Assert.True(File.Exists(FilePath));
        Assert.Equal(Content, await File.ReadAllTextAsync(FilePath));
    }

    [Fact]
    public async Task DownloadAsync_VerifiesSha1()
    {
        var Content = "hello world";
        var Bytes = System.Text.Encoding.UTF8.GetBytes(Content);

        var Handler = new FakeHttpMessageHandler((Request, CancellationToken) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Bytes)
            });
        });

        var HttpClient = new HttpClient(Handler);
        var MetadataHttpClient = new MetadataHttpClient(HttpClient, new MetadataHttpOptions());
        var Downloader = new HttpDownloader(MetadataHttpClient);

        string Sha1;
        using (var Algorithm = SHA1.Create()) {
            Sha1 = Convert.ToHexString(Algorithm.ComputeHash(Bytes));
        }

        var RootDirectory = Path.Combine(Path.GetTempPath(), "BlockiumLauncherTests", Guid.NewGuid().ToString("N"));
        var FilePath = Path.Combine(RootDirectory, "downloads", "sample.bin");

        var Result = await Downloader.DownloadAsync(
            new DownloadRequest(new Uri("https://example.invalid/file"), FilePath, Sha1),
            CancellationToken.None);

        Assert.True(Result.IsSuccess);
        Assert.True(File.Exists(FilePath));
    }
}
