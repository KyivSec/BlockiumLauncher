using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Diagnostics;

public sealed class NoOpJavaRuntimeResolver : IJavaRuntimeResolver
{
    public static readonly NoOpJavaRuntimeResolver Instance = new();

    public Task<Result<string>> ResolveExecutablePathAsync(string MinecraftVersion, CancellationToken CancellationToken)
    {
        return Task.FromResult(Result<string>.Failure(new Error(
            "Java.ResolveUnavailable",
            "Automatic Java resolution is not available.")));
    }
}