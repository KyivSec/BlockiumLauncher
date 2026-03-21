using BlockiumLauncher.Shared.Errors;

namespace BlockiumLauncher.Application.UseCases.Java;

public static class JavaErrors
{
    public static Error NotFound(string Message, string? Details = null)
    {
        return new Error("Java.NotFound", Message, Details);
    }

    public static Error Invalid(string Message, string? Details = null)
    {
        return new Error("Java.Invalid", Message, Details);
    }

    public static Error Timeout(string Message, string? Details = null)
    {
        return new Error("Java.Timeout", Message, Details);
    }

    public static Error VersionProbeFailed(string Message, string? Details = null)
    {
        return new Error("Java.VersionProbeFailed", Message, Details);
    }

    public static Error AccessDenied(string Message, string? Details = null)
    {
        return new Error("Java.AccessDenied", Message, Details);
    }
}
