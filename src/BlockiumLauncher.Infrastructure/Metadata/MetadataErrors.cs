using BlockiumLauncher.Shared.Errors;

namespace BlockiumLauncher.Infrastructure.Metadata;

internal static class MetadataErrors
{
    internal static Error HttpFailed(string Message, string? Details = null)
    {
        return new Error("Metadata.HttpFailed", Message, Details);
    }

    internal static Error Timeout(string Message, string? Details = null)
    {
        return new Error("Metadata.Timeout", Message, Details);
    }

    internal static Error InvalidPayload(string Message, string? Details = null)
    {
        return new Error("Metadata.InvalidPayload", Message, Details);
    }

    internal static Error UnsupportedLoaderType(string Message, string? Details = null)
    {
        return new Error("Loader.UnsupportedType", Message, Details);
    }

    internal static Error NotFound(string Message, string? Details = null)
    {
        return new Error("Metadata.NotFound", Message, Details);
    }

    internal static Error HashMismatch(string Message, string? Details = null)
    {
        return new Error("Download.HashMismatch", Message, Details);
    }
}
