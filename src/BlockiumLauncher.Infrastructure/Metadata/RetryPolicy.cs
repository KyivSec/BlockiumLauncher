using System.Net;

namespace BlockiumLauncher.Infrastructure.Metadata;

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
        if (CancellationToken.IsCancellationRequested) {
            return false;
        }

        return Exception is HttpRequestException
            || Exception is TaskCanceledException;
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
