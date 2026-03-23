using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Infrastructure.Java;

public interface IJavaRequirementResolver
{
    int GetRequiredJavaMajor(VersionId gameVersion, LoaderType loaderType);
    int GetRequiredJavaMajor(string gameVersion, LoaderType loaderType);
    bool IsSatisfiedBy(int installedJavaMajor, VersionId gameVersion, LoaderType loaderType);
    bool IsSatisfiedBy(int installedJavaMajor, string gameVersion, LoaderType loaderType);
}