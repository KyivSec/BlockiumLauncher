using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Infrastructure.Persistence.Paths;

public interface ILauncherPaths
{
    string RootDirectory { get; }
    string DataDirectory { get; }
    string CacheDirectory { get; }
    string InstancesDirectory { get; }

    string InstancesFilePath { get; }
    string AccountsFilePath { get; }
    string JavaInstallationsFilePath { get; }
    string VersionsCacheFilePath { get; }

    string GetLoaderVersionsCacheFilePath(LoaderType LoaderType, VersionId GameVersion);
}
