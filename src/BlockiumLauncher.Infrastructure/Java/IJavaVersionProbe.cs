using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Java;

public interface IJavaVersionProbe
{
    Task<Result<JavaVersionParseResult>> ProbeAsync(string ExecutablePath, CancellationToken CancellationToken);
}
