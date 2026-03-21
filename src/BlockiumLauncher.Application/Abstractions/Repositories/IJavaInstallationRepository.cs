using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.Abstractions.Repositories;

public interface IJavaInstallationRepository
{
    Task<IReadOnlyList<JavaInstallation>> ListAsync(CancellationToken CancellationToken);
    Task<JavaInstallation?> GetByIdAsync(JavaInstallationId JavaInstallationId, CancellationToken CancellationToken);
    Task SaveAsync(JavaInstallation JavaInstallation, CancellationToken CancellationToken);
    Task DeleteAsync(JavaInstallationId JavaInstallationId, CancellationToken CancellationToken);
}
