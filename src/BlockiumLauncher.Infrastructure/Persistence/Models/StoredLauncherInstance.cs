using BlockiumLauncher.Domain.Enums;

namespace BlockiumLauncher.Infrastructure.Persistence.Models;

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
