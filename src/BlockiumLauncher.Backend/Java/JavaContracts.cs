using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Services
{
    public interface IJavaDiscoveryService
    {
        Task<Result<IReadOnlyList<JavaInstallation>>> DiscoverAsync(bool IncludeInvalid, CancellationToken CancellationToken);
    }

    public interface IJavaRuntimeResolver
    {
        Task<Result<string>> ResolveExecutablePathAsync(
            string MinecraftVersion,
            LoaderType loaderType,
            int? preferredJavaMajor = null,
            bool skipCompatibilityChecks = false,
            CancellationToken CancellationToken = default);
    }

    public interface IManagedJavaRuntimeService
    {
        Task<Result<JavaInstallation?>> GetInstalledRuntimeAsync(int javaMajor, CancellationToken cancellationToken);
        Task<Result<JavaInstallation>> InstallRuntimeAsync(int javaMajor, bool forceReinstall, CancellationToken cancellationToken);
    }

    public interface IJavaValidationService
    {
        Task<Result<JavaInstallation>> ValidateInstallationAsync(JavaInstallation JavaInstallation, CancellationToken CancellationToken);
        Task<Result<JavaInstallation>> ValidateExecutableAsync(string ExecutablePath, CancellationToken CancellationToken);
    }
}

namespace BlockiumLauncher.Infrastructure.Java
{
    public interface IJavaRequirementResolver
    {
        int GetRequiredJavaMajor(VersionId gameVersion, LoaderType loaderType);
        int GetRequiredJavaMajor(string gameVersion, LoaderType loaderType);
        bool IsSatisfiedBy(int installedJavaMajor, VersionId gameVersion, LoaderType loaderType);
        bool IsSatisfiedBy(int installedJavaMajor, string gameVersion, LoaderType loaderType);
    }
}
