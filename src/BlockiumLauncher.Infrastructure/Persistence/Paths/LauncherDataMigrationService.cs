namespace BlockiumLauncher.Infrastructure.Persistence.Paths;

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