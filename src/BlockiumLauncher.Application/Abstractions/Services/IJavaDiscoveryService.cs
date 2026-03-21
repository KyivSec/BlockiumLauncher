using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Services;

public interface IJavaDiscoveryService
{
    Task<Result<IReadOnlyList<JavaInstallation>>> DiscoverAsync(bool IncludeInvalid, CancellationToken CancellationToken);
}
