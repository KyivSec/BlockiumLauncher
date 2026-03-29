using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Domain.Entities;

public sealed class LauncherInstance
{
    public InstanceId InstanceId { get; }
    public string Name { get; private set; }
    public VersionId GameVersion { get; private set; }
    public LoaderType LoaderType { get; private set; }
    public VersionId? LoaderVersion { get; private set; }
    public InstanceState State { get; private set; }
    public string InstallLocation { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset? LastPlayedAtUtc { get; private set; }
    public LaunchProfile LaunchProfile { get; private set; }
    public string? IconKey { get; private set; }

    private LauncherInstance(
        InstanceId InstanceId,
        string Name,
        VersionId GameVersion,
        LoaderType LoaderType,
        VersionId? LoaderVersion,
        string InstallLocation,
        DateTimeOffset CreatedAtUtc,
        LaunchProfile LaunchProfile,
        string? IconKey)
    {
        this.InstanceId = InstanceId;
        this.Name = NormalizeRequired(Name, nameof(Name));
        this.GameVersion = GameVersion;
        this.LoaderType = LoaderType;
        this.LoaderVersion = LoaderVersion;
        this.InstallLocation = NormalizeRequired(InstallLocation, nameof(InstallLocation));
        this.CreatedAtUtc = CreatedAtUtc;
        this.LaunchProfile = LaunchProfile ?? throw new ArgumentNullException(nameof(LaunchProfile));
        this.IconKey = NormalizeOptional(IconKey);
        State = InstanceState.Created;

        ValidateLoader(LoaderType, LoaderVersion);
    }

    public static LauncherInstance Create(
        InstanceId InstanceId,
        string Name,
        VersionId GameVersion,
        LoaderType LoaderType,
        VersionId? LoaderVersion,
        string InstallLocation,
        DateTimeOffset CreatedAtUtc,
        LaunchProfile LaunchProfile,
        string? IconKey = null)
    {
        return new(
            InstanceId,
            Name,
            GameVersion,
            LoaderType,
            LoaderVersion,
            InstallLocation,
            CreatedAtUtc,
            LaunchProfile,
            IconKey);
    }

    public void Rename(string Name)
    {
        EnsureNotDeleted();
        this.Name = NormalizeRequired(Name, nameof(Name));
    }

    public void ChangeLaunchProfile(LaunchProfile LaunchProfile)
    {
        EnsureNotDeleted();
        this.LaunchProfile = LaunchProfile ?? throw new ArgumentNullException(nameof(LaunchProfile));
    }

    public void ChangeIconKey(string? IconKey)
    {
        EnsureNotDeleted();
        this.IconKey = NormalizeOptional(IconKey);
    }

    public void MarkInstalled()
    {
        EnsureNotDeleted();
        State = InstanceState.Installed;
    }

    public void MarkNeedsRepair()
    {
        EnsureNotDeleted();
        State = InstanceState.NeedsRepair;
    }

    public void MarkBroken()
    {
        EnsureNotDeleted();
        State = InstanceState.Broken;
    }

    public void MarkUpdating()
    {
        EnsureNotDeleted();
        State = InstanceState.Updating;
    }

    public void MarkDeleted()
    {
        State = InstanceState.Deleted;
    }

    public void RecordLaunch(DateTimeOffset TimestampUtc)
    {
        if (State is not InstanceState.Installed)
        {
            throw new InvalidOperationException("Only installed instances can record launches.");
        }

        LastPlayedAtUtc = TimestampUtc;
    }

    private static void ValidateLoader(LoaderType LoaderType, VersionId? LoaderVersion)
    {
        if (LoaderType == LoaderType.Vanilla && LoaderVersion is not null)
        {
            throw new ArgumentException("Vanilla instances cannot define a loader version.", nameof(LoaderVersion));
        }

        if (LoaderType != LoaderType.Vanilla && LoaderVersion is null)
        {
            throw new ArgumentException("Modded instances require a loader version.", nameof(LoaderVersion));
        }
    }

    private void EnsureNotDeleted()
    {
        if (State == InstanceState.Deleted)
        {
            throw new InvalidOperationException("Deleted instances cannot be modified.");
        }
    }

    private static string NormalizeRequired(string Value, string ParameterName)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", ParameterName);
        }

        return Value.Trim();
    }

    private static string? NormalizeOptional(string? Value)
    {
        return string.IsNullOrWhiteSpace(Value) ? null : Value.Trim();
    }
}
