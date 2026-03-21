namespace BlockiumLauncher.Infrastructure.Persistence.Models;

public sealed class StoredLaunchProfile
{
    public int MinMemoryMb { get; set; }
    public int MaxMemoryMb { get; set; }
    public List<string> ExtraJvmArgs { get; set; } = [];
    public List<string> ExtraGameArgs { get; set; } = [];
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new(StringComparer.Ordinal);
}
