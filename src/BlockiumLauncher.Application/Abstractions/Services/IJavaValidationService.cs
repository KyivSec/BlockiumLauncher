using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Services;

public interface IJavaValidationService
{
    Task<Result<JavaInstallation>> ValidateInstallationAsync(JavaInstallation JavaInstallation, CancellationToken CancellationToken);
    Task<Result<JavaInstallation>> ValidateExecutableAsync(string ExecutablePath, CancellationToken CancellationToken);
}
