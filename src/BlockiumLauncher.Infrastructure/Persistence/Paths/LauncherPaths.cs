using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Infrastructure.Persistence.Paths;

public sealed class LauncherPaths : ILauncherPaths
{
    public string RootDirectory { get; }
    public string DataDirectory { get; }
    public string CacheDirectory { get; }
    public string InstancesDirectory { get; }

    public string SharedDirectory { get; }
    public string SharedVersionsDirectory { get; }
    public string SharedLibrariesDirectory { get; }
    public string SharedAssetsDirectory { get; }
    public string SharedAssetIndexesDirectory { get; }
    public string SharedAssetObjectsDirectory { get; }
    public string SharedLoadersDirectory { get; }
    public string SharedNativesDirectory { get; }
    public string LogsDirectory { get; }
    public string RuntimesDirectory { get; }
    public string ManagedJavaDirectory { get; }

    public string InstancesFilePath { get; }
    public string AccountsFilePath { get; }
    public string JavaInstallationsFilePath { get; }
    public string VersionsCacheFilePath { get; }

    public LauncherPaths(string RootDirectory)
    {
        if (string.IsNullOrWhiteSpace(RootDirectory)) {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(RootDirectory));
        }

        this.RootDirectory = RootDirectory.Trim();
        DataDirectory = Path.Combine(this.RootDirectory, "data");
        CacheDirectory = Path.Combine(this.RootDirectory, "cache");
        InstancesDirectory = Path.Combine(this.RootDirectory, "instances");

        SharedDirectory = Path.Combine(this.RootDirectory, "shared");
        SharedVersionsDirectory = Path.Combine(SharedDirectory, "versions");
        SharedLibrariesDirectory = Path.Combine(SharedDirectory, "libraries");
        SharedAssetsDirectory = Path.Combine(SharedDirectory, "assets");
        SharedAssetIndexesDirectory = Path.Combine(SharedAssetsDirectory, "indexes");
        SharedAssetObjectsDirectory = Path.Combine(SharedAssetsDirectory, "objects");
        SharedLoadersDirectory = Path.Combine(SharedDirectory, "loaders");
        SharedNativesDirectory = Path.Combine(SharedDirectory, "natives");
        LogsDirectory = Path.Combine(this.RootDirectory, "logs");
        RuntimesDirectory = Path.Combine(this.RootDirectory, "runtimes");
        ManagedJavaDirectory = Path.Combine(RuntimesDirectory, "java");

        InstancesFilePath = Path.Combine(DataDirectory, "instances.json");
        AccountsFilePath = Path.Combine(DataDirectory, "accounts.json");
        JavaInstallationsFilePath = Path.Combine(DataDirectory, "java-installations.json");
        VersionsCacheFilePath = Path.Combine(CacheDirectory, "versions.json");

        EnsureDirectoryLayout();
    }

    public static LauncherPaths CreateDefault()
    {
        return new LauncherPaths(GetDefaultRootDirectory());
    }

    public string GetLoaderVersionsCacheFilePath(LoaderType LoaderType, VersionId GameVersion)
    {
        var LoaderName = LoaderType.ToString().ToLowerInvariant();
        var VersionName = SanitizeFileName(GameVersion.ToString());

        return Path.Combine(CacheDirectory, "loaders", $"{LoaderName}-{VersionName}.json");
    }

    public string GetSharedVersionDirectory(string GameVersion)
    {
        return Path.Combine(SharedVersionsDirectory, SanitizeFileName(GameVersion));
    }

    public string GetSharedVersionJsonPath(string GameVersion)
    {
        return Path.Combine(GetSharedVersionDirectory(GameVersion), $"{SanitizeFileName(GameVersion)}.json");
    }

    public string GetSharedClientJarPath(string GameVersion)
    {
        return Path.Combine(GetSharedVersionDirectory(GameVersion), $"{SanitizeFileName(GameVersion)}.jar");
    }

    public string GetSharedNativesDirectory(string RuntimeKey)
    {
        return Path.Combine(SharedNativesDirectory, SanitizeFileName(RuntimeKey));
    }

    public string GetSharedLoaderDirectory(LoaderType LoaderType, string GameVersion, string LoaderVersion)
    {
        return Path.Combine(
            SharedLoadersDirectory,
            LoaderType.ToString().ToLowerInvariant(),
            SanitizeFileName(GameVersion),
            SanitizeFileName(LoaderVersion));
    }

    public string GetManagedJavaDirectory(string RuntimeKey)
    {
        return Path.Combine(ManagedJavaDirectory, SanitizeFileName(RuntimeKey));
    }

    private void EnsureDirectoryLayout()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(InstancesDirectory);
        Directory.CreateDirectory(SharedDirectory);
        Directory.CreateDirectory(SharedVersionsDirectory);
        Directory.CreateDirectory(SharedLibrariesDirectory);
        Directory.CreateDirectory(SharedAssetsDirectory);
        Directory.CreateDirectory(SharedAssetIndexesDirectory);
        Directory.CreateDirectory(SharedAssetObjectsDirectory);
        Directory.CreateDirectory(SharedLoadersDirectory);
        Directory.CreateDirectory(SharedNativesDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(RuntimesDirectory);
        Directory.CreateDirectory(ManagedJavaDirectory);
    }

    private static string GetDefaultRootDirectory()
    {
        if (OperatingSystem.IsWindows()) {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BlockiumLauncher");
        }

        if (OperatingSystem.IsMacOS()) {
            var Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(Home, "Library", "Application Support", "BlockiumLauncher");
        }

        var XdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(XdgDataHome)) {
            return Path.Combine(XdgDataHome, "BlockiumLauncher");
        }

        var UserHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(UserHome, ".local", "share", "BlockiumLauncher");
    }

    private static string SanitizeFileName(string Value)
    {
        var InvalidChars = Path.GetInvalidFileNameChars();
        var Buffer = Value.ToCharArray();

        for (var Index = 0; Index < Buffer.Length; Index++) {
            if (InvalidChars.Contains(Buffer[Index])) {
                Buffer[Index] = '-';
            }
        }

        return new string(Buffer);
    }
}