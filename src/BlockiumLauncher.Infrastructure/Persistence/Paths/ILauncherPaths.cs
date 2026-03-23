using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Infrastructure.Persistence.Paths;

public interface ILauncherPaths
{
    string RootDirectory { get; }
    string DataDirectory { get; }
    string CacheDirectory { get; }
    string InstancesDirectory { get; }

    string SharedDirectory { get; }
    string SharedVersionsDirectory { get; }
    string SharedLibrariesDirectory { get; }
    string SharedAssetsDirectory { get; }
    string SharedAssetIndexesDirectory { get; }
    string SharedAssetObjectsDirectory { get; }
    string SharedLoadersDirectory { get; }
    string SharedNativesDirectory { get; }

    string LogsDirectory { get; }

    string RuntimesDirectory { get; }
    string ManagedJavaDirectory { get; }

    string InstancesFilePath { get; }
    string AccountsFilePath { get; }
    string JavaInstallationsFilePath { get; }
    string VersionsCacheFilePath { get; }

    string GetLoaderVersionsCacheFilePath(LoaderType loaderType, VersionId gameVersion);
    string GetSharedVersionDirectory(string gameVersion);
    string GetSharedVersionJsonPath(string gameVersion);
    string GetSharedClientJarPath(string gameVersion);
    string GetSharedNativesDirectory(string runtimeKey);
    string GetSharedLoaderDirectory(LoaderType loaderType, string gameVersion, string loaderVersion);
    string GetManagedJavaDirectory(string runtimeKey);
}