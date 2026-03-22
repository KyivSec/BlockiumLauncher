using BlockiumLauncher.Shared.Errors;

namespace BlockiumLauncher.Application.UseCases.Launch;

public static class LaunchErrors
{
    public static readonly Error InvalidRequest = new("Launch.InvalidRequest", "The launch request is invalid.");
    public static readonly Error InstanceNotFound = new("Launch.InstanceNotFound", "The requested instance was not found.");
    public static readonly Error InstanceDirectoryMissing = new("Launch.InstanceDirectoryMissing", "The instance directory does not exist.");
    public static readonly Error JavaExecutableMissing = new("Launch.JavaExecutableMissing", "The Java executable path does not exist.");
    public static readonly Error MainClassMissing = new("Launch.MainClassMissing", "The launch main class is required.");
    public static readonly Error VersionMetadataMissing = new("Launch.VersionMetadataMissing", "The requested game version metadata could not be resolved.");
    public static readonly Error LoaderMetadataMissing = new("Launch.LoaderMetadataMissing", "The requested loader metadata could not be resolved.");
    public static readonly Error AssetsDirectoryMissing = new("Launch.AssetsDirectoryMissing", "The assets directory does not exist.");
    public static readonly Error AssetIndexMissing = new("Launch.AssetIndexMissing", "The asset index identifier is required when assets directory is provided.");
    public static readonly Error ClasspathMissing = new("Launch.ClasspathMissing", "At least one classpath entry is required.");
    public static readonly Error ClasspathEntryMissing = new("Launch.ClasspathEntryMissing", "A classpath entry does not exist.");
    public static readonly Error ProcessStartFailed = new("Launch.ProcessStartFailed", "The launch process could not be started.");
    public static readonly Error LaunchSessionNotFound = new("Launch.LaunchSessionNotFound", "The requested launch session was not found.");
    public static readonly Error StopFailed = new("Launch.StopFailed", "The running launch could not be stopped.");
}