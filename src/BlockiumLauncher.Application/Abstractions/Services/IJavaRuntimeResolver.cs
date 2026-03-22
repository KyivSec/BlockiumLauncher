using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Services;

public interface IJavaRuntimeResolver
{
    Task<Result<string>> ResolveExecutablePathAsync(string MinecraftVersion, CancellationToken CancellationToken);
}