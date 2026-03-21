using System.Net;
using BlockiumLauncher.Infrastructure.Metadata;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Metadata;

public sealed class MetadataHttpClientTests
{
    [Fact]
    public async Task GetStringAsync_ReturnsBody_OnSuccess()
    {
        var Handler = new FakeHttpMessageHandler((Request, CancellationToken) =>
        {
            var Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("hello")
            };

            return Task.FromResult(Response);
        });

        var HttpClient = new HttpClient(Handler);
        var Client = new MetadataHttpClient(HttpClient, new MetadataHttpOptions());

        var Result = await Client.GetStringAsync(new Uri("https://example.invalid/test"), CancellationToken.None);

        Assert.True(Result.IsSuccess);
        Assert.Equal("hello", Result.Value);
    }

    [Fact]
    public async Task GetStringAsync_Retries_OnTransientServerError()
    {
        var CallCount = 0;

        var Handler = new FakeHttpMessageHandler((Request, CancellationToken) =>
        {
            CallCount++;

            if (CallCount == 1) {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok")
            });
        });

        var HttpClient = new HttpClient(Handler);
        var Client = new MetadataHttpClient(HttpClient, new MetadataHttpOptions());

        var Result = await Client.GetStringAsync(new Uri("https://example.invalid/test"), CancellationToken.None);

        Assert.True(Result.IsSuccess);
        Assert.Equal("ok", Result.Value);
        Assert.Equal(2, CallCount);
    }
}
