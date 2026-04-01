using BlockiumLauncher.Domain.Enums;

namespace BlockiumLauncher.Infrastructure.Persistence.Models;

public sealed class StoredJavaInstallation
{
    public string JavaInstallationId { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public JavaArchitecture Architecture { get; set; }
    public string Vendor { get; set; } = string.Empty;
    public bool IsValid { get; set; }
}

public sealed class StoredLaunchProfile
{
    public int MinMemoryMb { get; set; }
    public int MaxMemoryMb { get; set; }
    public List<string> ExtraJvmArgs { get; set; } = [];
    public List<string> ExtraGameArgs { get; set; } = [];
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new(StringComparer.Ordinal);
}

public sealed class StoredLauncherAccount
{
    public string AccountId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? AccountIdentifier { get; set; }
    public string? AccessTokenRef { get; set; }
    public string? RefreshTokenRef { get; set; }
    public bool IsDefault { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? ValidatedAtUtc { get; set; }
}

public sealed class StoredSkinAsset
{
    public string SkinId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ModelType { get; set; } = string.Empty;
    public DateTimeOffset ImportedAtUtc { get; set; }
}

public sealed class StoredCapeAsset
{
    public string CapeId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTimeOffset ImportedAtUtc { get; set; }
}

public sealed class StoredAccountAppearance
{
    public string AccountId { get; set; } = string.Empty;
    public string? SelectedSkinId { get; set; }
    public string? SelectedCapeId { get; set; }
}

public sealed class StoredSkinLibrary
{
    public List<StoredSkinAsset> Skins { get; set; } = [];
    public List<StoredCapeAsset> Capes { get; set; } = [];
}

public sealed class StoredLauncherInstance
{
    public string InstanceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string GameVersion { get; set; } = string.Empty;
    public LoaderType LoaderType { get; set; }
    public string? LoaderVersion { get; set; }
    public InstanceState State { get; set; }
    public string InstallLocation { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? LastPlayedAtUtc { get; set; }
    public StoredLaunchProfile LaunchProfile { get; set; } = new();
    public string? IconKey { get; set; }
}

public sealed class StoredLoaderVersionCache
{
    public List<StoredLoaderVersionSummary> LoaderVersions { get; set; } = [];
}

public sealed class StoredLoaderVersionSummary
{
    public LoaderType LoaderType { get; set; }
    public string GameVersion { get; set; } = string.Empty;
    public string LoaderVersion { get; set; } = string.Empty;
    public bool IsStable { get; set; }
}

public sealed class StoredVersionCache
{
    public List<StoredVersionSummary> Versions { get; set; } = [];
}

public sealed class StoredVersionSummary
{
    public string VersionId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsRelease { get; set; }
    public DateTimeOffset ReleasedAtUtc { get; set; }
}
