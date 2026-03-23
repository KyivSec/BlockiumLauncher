using BlockiumLauncher.Domain.Enums;

namespace BlockiumLauncher.Infrastructure.Storage;

public interface ISharedContentLayout
{
    string RootDirectory { get; }
    string VersionsDirectory { get; }
    string LibrariesDirectory { get; }
    string AssetsDirectory { get; }
    string AssetIndexesDirectory { get; }
    string AssetObjectsDirectory { get; }
    string LoadersDirectory { get; }
    string NativesDirectory { get; }
    string LogsDirectory { get; }
    string JavaRuntimesDirectory { get; }

    string GetSharedVersionDirectory(string gameVersion);
    string GetSharedVersionJsonPath(string gameVersion);
    string GetSharedClientJarPath(string gameVersion);
    string GetSharedLoaderDirectory(LoaderType loaderType, string gameVersion, string loaderVersion);
    string GetSharedNativesDirectory(string runtimeKey);
    string GetJavaRuntimeDirectory(string runtimeKey);
}