using BlockiumLauncher.Shared.Errors;

namespace BlockiumLauncher.Application.UseCases.Install;

public static class InstallErrors
{
    public static readonly Error InvalidRequest = new("Install.InvalidRequest", "The install request is invalid.");
    public static readonly Error VersionNotFound = new("Install.VersionNotFound", "The requested game version was not found.");
    public static readonly Error LoaderNotFound = new("Install.LoaderNotFound", "The requested loader version was not found.");
    public static readonly Error InstanceAlreadyExists = new("Install.InstanceAlreadyExists", "An instance with the same name or path already exists.");
    public static readonly Error InstanceNotFound = new("Install.InstanceNotFound", "The requested instance was not found.");
    public static readonly Error TargetPathInvalid = new("Install.TargetPathInvalid", "The target path is invalid.");
    public static readonly Error DownloadFailed = new("Install.DownloadFailed", "Failed to prepare install content.");
    public static readonly Error ExtractFailed = new("Install.ExtractFailed", "Failed to extract archive content.");
    public static readonly Error CommitFailed = new("Install.CommitFailed", "Failed to commit staged content.");
    public static readonly Error RollbackFailed = new("Install.RollbackFailed", "Failed to rollback staged content.");
    public static readonly Error ImportSourceMissing = new("Install.ImportSourceMissing", "The import source directory does not exist.");
    public static readonly Error ImportInvalidStructure = new("Install.ImportInvalidStructure", "The import source directory is not a valid instance structure.");
    public static readonly Error PersistenceFailed = new("Install.PersistenceFailed", "Failed to persist instance metadata.");
    public static readonly Error Unexpected = new("Install.Unexpected", "The install workflow failed unexpectedly.");
}