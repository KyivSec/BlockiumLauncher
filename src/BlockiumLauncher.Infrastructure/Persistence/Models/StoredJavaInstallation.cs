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
