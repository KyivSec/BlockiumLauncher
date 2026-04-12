using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.Abstractions.Paths
{
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
        string DiagnosticsDirectory { get; }
        string LatestLogFilePath { get; }

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
        string GetDefaultInstanceDirectory(string instanceName);
        string GetInstanceDataDirectory(string installLocation);
        string GetInstanceMetadataFilePath(string installLocation);
        string GetInstanceModpackMetadataFilePath(string installLocation);
        string GetContextLogFilePath(string context, DateTimeOffset? timestampUtc = null);
    }
}

namespace BlockiumLauncher.Application.Abstractions.Services
{
    public interface IClock
    {
        DateTimeOffset UtcNow { get; }
    }
}
