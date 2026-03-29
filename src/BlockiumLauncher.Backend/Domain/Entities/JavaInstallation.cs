using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Domain.Entities;

public sealed class JavaInstallation
{
    public JavaInstallationId JavaInstallationId { get; }
    public string ExecutablePath { get; }
    public string Version { get; }
    public JavaArchitecture Architecture { get; }
    public string Vendor { get; }
    public bool IsValid { get; private set; }

    private JavaInstallation(
        JavaInstallationId JavaInstallationId,
        string ExecutablePath,
        string Version,
        JavaArchitecture Architecture,
        string Vendor,
        bool IsValid)
    {
        this.JavaInstallationId = JavaInstallationId;
        this.ExecutablePath = NormalizeRequired(ExecutablePath, nameof(ExecutablePath));
        this.Version = NormalizeRequired(Version, nameof(Version));
        this.Architecture = Architecture;
        this.Vendor = NormalizeRequired(Vendor, nameof(Vendor));
        this.IsValid = IsValid;
    }

    public static JavaInstallation Create(
        JavaInstallationId JavaInstallationId,
        string ExecutablePath,
        string Version,
        JavaArchitecture Architecture,
        string Vendor,
        bool IsValid = true)
    {
        return new(
            JavaInstallationId,
            ExecutablePath,
            Version,
            Architecture,
            Vendor,
            IsValid);
    }

    public void MarkValid()
    {
        IsValid = true;
    }

    public void MarkInvalid()
    {
        IsValid = false;
    }

    private static string NormalizeRequired(string Value, string ParameterName)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", ParameterName);
        }

        return Value.Trim();
    }
}
