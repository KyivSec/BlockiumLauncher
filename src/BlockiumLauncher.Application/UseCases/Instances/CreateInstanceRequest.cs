using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Instances;

public sealed class CreateInstanceRequest
{
    public string Name { get; }
    public VersionId GameVersion { get; }
    public LoaderType LoaderType { get; }
    public string? LoaderVersion { get; }
    public string InstallLocation { get; }
    public LaunchProfile LaunchProfile { get; }
    public string? IconKey { get; }

    public CreateInstanceRequest(
        string Name,
        VersionId GameVersion,
        LoaderType LoaderType,
        string? LoaderVersion,
        string InstallLocation,
        LaunchProfile? LaunchProfile = null,
        string? IconKey = null)
    {
        this.Name = NormalizeRequired(Name, nameof(Name));
        this.GameVersion = GameVersion;
        this.LoaderType = LoaderType;
        this.LoaderVersion = NormalizeOptional(LoaderVersion);
        this.InstallLocation = NormalizeRequired(InstallLocation, nameof(InstallLocation));
        this.LaunchProfile = LaunchProfile ?? LaunchProfile.CreateDefault();
        this.IconKey = NormalizeOptional(IconKey);

        ValidateLoader();
    }

    private void ValidateLoader()
    {
        if (LoaderType == LoaderType.Vanilla && LoaderVersion is not null)
        {
            throw new ArgumentException("Vanilla instances must not specify a loader version.", nameof(LoaderVersion));
        }

        if (LoaderType != LoaderType.Vanilla && LoaderVersion is null)
        {
            throw new ArgumentException("Non-vanilla instances must specify a loader version.", nameof(LoaderVersion));
        }
    }

    private static string NormalizeRequired(string Value, string ParamName)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", ParamName);
        }

        return Value.Trim();
    }

    private static string? NormalizeOptional(string? Value)
    {
        return string.IsNullOrWhiteSpace(Value) ? null : Value.Trim();
    }
}
