using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Infrastructure.Persistence.Paths;

public sealed class LauncherPaths : ILauncherPaths
{
    public string RootDirectory { get; }
    public string DataDirectory { get; }
    public string CacheDirectory { get; }
    public string InstancesDirectory { get; }

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

        InstancesFilePath = Path.Combine(DataDirectory, "instances.json");
        AccountsFilePath = Path.Combine(DataDirectory, "accounts.json");
        JavaInstallationsFilePath = Path.Combine(DataDirectory, "java-installations.json");
        VersionsCacheFilePath = Path.Combine(CacheDirectory, "versions.json");
    }

    public string GetLoaderVersionsCacheFilePath(LoaderType LoaderType, VersionId GameVersion)
    {
        var LoaderName = LoaderType.ToString().ToLowerInvariant();
        var VersionName = SanitizeFileName(GameVersion.ToString());

        return Path.Combine(CacheDirectory, "loaders", $"{LoaderName}-{VersionName}.json");
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
