namespace BlockiumLauncher.Infrastructure.Tests.Metadata;

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Handler;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Handler)
    {
        this.Handler = Handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage Request, CancellationToken CancellationToken)
    {
        return Handler(Request, CancellationToken);
    }
}
