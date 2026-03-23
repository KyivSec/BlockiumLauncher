using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Infrastructure.Persistence.Paths;

namespace BlockiumLauncher.Infrastructure.Storage;

public sealed class SharedContentLayout : ISharedContentLayout
{
    private readonly ILauncherPaths _launcherPaths;

    public SharedContentLayout(ILauncherPaths launcherPaths)
    {
        _launcherPaths = launcherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
    }

    public string RootDirectory => _launcherPaths.SharedDirectory;
    public string VersionsDirectory => _launcherPaths.SharedVersionsDirectory;
    public string LibrariesDirectory => _launcherPaths.SharedLibrariesDirectory;
    public string AssetsDirectory => _launcherPaths.SharedAssetsDirectory;
    public string AssetIndexesDirectory => _launcherPaths.SharedAssetIndexesDirectory;
    public string AssetObjectsDirectory => _launcherPaths.SharedAssetObjectsDirectory;
    public string LoadersDirectory => _launcherPaths.SharedLoadersDirectory;
    public string NativesDirectory => _launcherPaths.SharedNativesDirectory;
    public string LogsDirectory => _launcherPaths.LogsDirectory;
    public string JavaRuntimesDirectory => _launcherPaths.ManagedJavaDirectory;

    public string GetSharedVersionDirectory(string gameVersion)
    {
        return _launcherPaths.GetSharedVersionDirectory(gameVersion);
    }

    public string GetSharedVersionJsonPath(string gameVersion)
    {
        return _launcherPaths.GetSharedVersionJsonPath(gameVersion);
    }

    public string GetSharedClientJarPath(string gameVersion)
    {
        return _launcherPaths.GetSharedClientJarPath(gameVersion);
    }

    public string GetSharedLoaderDirectory(LoaderType loaderType, string gameVersion, string loaderVersion)
    {
        return _launcherPaths.GetSharedLoaderDirectory(loaderType, gameVersion, loaderVersion);
    }

    public string GetSharedNativesDirectory(string runtimeKey)
    {
        return _launcherPaths.GetSharedNativesDirectory(runtimeKey);
    }

    public string GetJavaRuntimeDirectory(string runtimeKey)
    {
        return _launcherPaths.GetManagedJavaDirectory(runtimeKey);
    }
}