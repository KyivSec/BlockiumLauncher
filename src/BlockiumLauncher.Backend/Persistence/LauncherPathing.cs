using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Infrastructure.Persistence.Paths;

public interface ILauncherDataMigrationService
{
    void MigrateIfNeeded();
}

public interface ILauncherPaths : BlockiumLauncher.Application.Abstractions.Paths.ILauncherPaths
{
}

public sealed class LauncherDataMigrationService : ILauncherDataMigrationService
{
    private readonly ILauncherPaths LauncherPaths;

    public LauncherDataMigrationService(ILauncherPaths launcherPaths)
    {
        LauncherPaths = launcherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
    }

    public void MigrateIfNeeded()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var roamingRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlockiumLauncher");

        var localRoot = LauncherPaths.RootDirectory;

        if (string.IsNullOrWhiteSpace(roamingRoot) ||
            string.IsNullOrWhiteSpace(localRoot) ||
            !Directory.Exists(roamingRoot) ||
            string.Equals(
                Path.GetFullPath(roamingRoot).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(localRoot).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(localRoot);

        foreach (var directoryName in new[]
        {
            "data",
            "cache",
            "instances",
            "shared",
            "logs",
            "runtimes"
        })
        {
            var source = Path.Combine(roamingRoot, directoryName);
            var target = Path.Combine(localRoot, directoryName);

            if (!Directory.Exists(source))
            {
                continue;
            }

            if (Directory.Exists(target))
            {
                CopyDirectoryContents(source, target);
                continue;
            }

            try
            {
                Directory.Move(source, target);
            }
            catch
            {
                CopyDirectoryContents(source, target);
            }
        }

        TryDeleteIfEmpty(roamingRoot);
    }

    private static void CopyDirectoryContents(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(target, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, file);
            var targetPath = Path.Combine(target, relativePath);
            var targetDirectory = Path.GetDirectoryName(targetPath);

            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            if (!File.Exists(targetPath))
            {
                File.Move(file, targetPath);
            }
        }
    }

    private static void TryDeleteIfEmpty(string path)
    {
        try
        {
            if (Directory.Exists(path) &&
                !Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories).Any() &&
                !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path, recursive: false);
            }
        }
        catch
        {
        }
    }
}

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
    public string DiagnosticsDirectory { get; }
    public string LatestLogFilePath { get; }

    public string RuntimesDirectory { get; }
    public string ManagedJavaDirectory { get; }

    public string InstancesFilePath { get; }
    public string AccountsFilePath { get; }
    public string JavaInstallationsFilePath { get; }
    public string VersionsCacheFilePath { get; }

    public LauncherPaths(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(rootDirectory));
        }

        RootDirectory = Path.GetFullPath(rootDirectory.Trim());

        DataDirectory = Path.Combine(RootDirectory, "data");
        CacheDirectory = Path.Combine(RootDirectory, "cache");
        InstancesDirectory = Path.Combine(RootDirectory, "instances");

        SharedDirectory = Path.Combine(RootDirectory, "shared");
        SharedVersionsDirectory = Path.Combine(SharedDirectory, "versions");
        SharedLibrariesDirectory = Path.Combine(SharedDirectory, "libraries");
        SharedAssetsDirectory = Path.Combine(SharedDirectory, "assets");
        SharedAssetIndexesDirectory = Path.Combine(SharedAssetsDirectory, "indexes");
        SharedAssetObjectsDirectory = Path.Combine(SharedAssetsDirectory, "objects");
        SharedLoadersDirectory = Path.Combine(SharedDirectory, "loaders");
        SharedNativesDirectory = Path.Combine(SharedDirectory, "natives");

        LogsDirectory = Path.Combine(RootDirectory, "logs");
        DiagnosticsDirectory = Path.Combine(RootDirectory, "diagnostics");
        LatestLogFilePath = Path.Combine(LogsDirectory, "latest.log");

        RuntimesDirectory = Path.Combine(RootDirectory, "runtimes");
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

    public string GetLoaderVersionsCacheFilePath(LoaderType loaderType, VersionId gameVersion)
    {
        var loaderName = loaderType.ToString().ToLowerInvariant();
        var versionName = SanitizeFileName(gameVersion.ToString());

        return Path.Combine(CacheDirectory, "loaders", $"{loaderName}-{versionName}.json");
    }

    public string GetSharedVersionDirectory(string gameVersion)
    {
        return Path.Combine(SharedVersionsDirectory, SanitizeFileName(gameVersion));
    }

    public string GetSharedVersionJsonPath(string gameVersion)
    {
        var safeGameVersion = SanitizeFileName(gameVersion);
        return Path.Combine(GetSharedVersionDirectory(gameVersion), $"{safeGameVersion}.json");
    }

    public string GetSharedClientJarPath(string gameVersion)
    {
        var safeGameVersion = SanitizeFileName(gameVersion);
        return Path.Combine(GetSharedVersionDirectory(gameVersion), $"{safeGameVersion}.jar");
    }

    public string GetSharedNativesDirectory(string runtimeKey)
    {
        return Path.Combine(SharedNativesDirectory, SanitizeFileName(runtimeKey));
    }

    public string GetSharedLoaderDirectory(LoaderType loaderType, string gameVersion, string loaderVersion)
    {
        return Path.Combine(
            SharedLoadersDirectory,
            loaderType.ToString().ToLowerInvariant(),
            SanitizeFileName(gameVersion),
            SanitizeFileName(loaderVersion));
    }

    public string GetManagedJavaDirectory(string runtimeKey)
    {
        return Path.Combine(ManagedJavaDirectory, SanitizeFileName(runtimeKey));
    }

    public string GetDefaultInstanceDirectory(string instanceName)
    {
        return Path.Combine(InstancesDirectory, SanitizeDirectoryName(instanceName, "instance"));
    }

    public string GetInstanceDataDirectory(string installLocation)
    {
        return Path.Combine(Path.GetFullPath(installLocation), ".blockium");
    }

    public string GetInstanceMetadataFilePath(string installLocation)
    {
        return Path.Combine(GetInstanceDataDirectory(installLocation), "instance-metadata.json");
    }

    public string GetInstanceModpackMetadataFilePath(string installLocation)
    {
        return Path.Combine(GetInstanceDataDirectory(installLocation), "modpack-metadata.json");
    }

    public string GetContextLogFilePath(string context, DateTimeOffset? timestampUtc = null)
    {
        var effectiveTimestamp = timestampUtc ?? DateTimeOffset.UtcNow;
        var safeContext = SanitizeDirectoryName(context, "launcher");
        return Path.Combine(LogsDirectory, $"{safeContext}_{effectiveTimestamp:yyyyMMdd}.log");
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
        Directory.CreateDirectory(DiagnosticsDirectory);

        Directory.CreateDirectory(RuntimesDirectory);
        Directory.CreateDirectory(ManagedJavaDirectory);

        Directory.CreateDirectory(Path.Combine(CacheDirectory, "loaders"));
    }

    private static string GetDefaultRootDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "BlockiumLauncher");
        }

        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "BlockiumLauncher");
        }

        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
        {
            return Path.Combine(xdgDataHome, "BlockiumLauncher");
        }

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userHome, ".local", "share", "BlockiumLauncher");
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var buffer = value.ToCharArray();

        for (var index = 0; index < buffer.Length; index++)
        {
            if (invalidChars.Contains(buffer[index]))
            {
                buffer[index] = '-';
            }
        }

        return new string(buffer);
    }

    private static string SanitizeDirectoryName(string value, string fallback)
    {
        var sanitized = SanitizeFileName(value)
            .Trim()
            .Replace(' ', '_');

        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }
}
