using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Errors;

namespace BlockiumLauncher.Application.UseCases.Install
{
    public sealed class FileVerificationIssue
    {
        public FileVerificationIssueKind Kind { get; init; }
        public string Path { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }

    public enum FileVerificationIssueKind
    {
        RootDirectoryMissing = 1,
        MinecraftDirectoryMissing = 2,
        BlockiumDirectoryMissing = 3
    }

    public sealed class FileVerificationResult
    {
        public LauncherInstance Instance { get; init; } = default!;
        public bool IsValid { get; init; }
        public IReadOnlyList<FileVerificationIssue> Issues { get; init; } = [];
    }

    public sealed class ImportInstanceRequest
    {
        public string SourceDirectory { get; init; } = string.Empty;
        public string InstanceName { get; init; } = string.Empty;
        public string? TargetDirectory { get; init; }
        public bool CopyInsteadOfMove { get; init; } = true;
    }

    public sealed class ImportInstanceResult
    {
        public LauncherInstance Instance { get; init; } = default!;
        public string InstalledPath { get; init; } = string.Empty;
    }

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

    public sealed class InstallInstanceRequest
    {
        public string InstanceName { get; init; } = string.Empty;
        public string GameVersion { get; init; } = string.Empty;
        public LoaderType LoaderType { get; init; }
        public string? LoaderVersion { get; init; }
        public string? TargetDirectory { get; init; }
        public bool OverwriteIfExists { get; init; }
        public bool DownloadRuntime { get; init; }
    }

    public sealed class InstallInstanceResult
    {
        public LauncherInstance Instance { get; init; } = default!;
        public string InstalledPath { get; init; } = string.Empty;
    }

    public sealed class InstallPlan
    {
        public string InstanceName { get; init; } = string.Empty;
        public string GameVersion { get; init; } = string.Empty;
        public LoaderType LoaderType { get; init; }
        public string? LoaderVersion { get; init; }
        public string TargetDirectory { get; init; } = string.Empty;
        public bool OverwriteIfExists { get; init; }
        public bool DownloadRuntime { get; init; }
        public IReadOnlyList<InstallPlanStep> Steps { get; init; } = [];
    }

    public sealed class InstallPlanStep
    {
        public InstallPlanStepKind Kind { get; init; }
        public string Source { get; init; } = string.Empty;
        public string Destination { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
    }

    public enum InstallPlanStepKind
    {
        CreateDirectory = 1,
        WriteMetadata = 2
    }

    public sealed class RepairInstanceRequest
    {
        public InstanceId InstanceId { get; init; }
    }

    public sealed class RepairInstanceResult
    {
        public LauncherInstance Instance { get; init; } = default!;
        public bool Changed { get; init; }
        public IReadOnlyList<string> RepairedPaths { get; init; } = [];
        public FileVerificationResult Verification { get; init; } = default!;
    }

    public sealed class UpdateInstanceRequest
    {
        public InstanceId InstanceId { get; init; } = default!;
        public VersionId TargetGameVersion { get; init; }
        public LoaderType TargetLoaderType { get; init; }
        public VersionId? TargetLoaderVersion { get; init; }
    }

    public sealed class UpdatePlan
    {
        public LauncherInstance Instance { get; init; } = default!;
        public bool IsNoOp { get; init; }
        public bool RequiresRepair { get; init; }
        public IReadOnlyList<UpdatePlanStep> Steps { get; init; } = [];
    }

    public sealed class UpdatePlanStep
    {
        public UpdatePlanStepKind Kind { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public enum UpdatePlanStepKind
    {
        VerifyInstance = 1,
        RepairStructure = 2,
        UpdateManagedContent = 3,
        PersistMetadata = 4,
        NoOp = 5
    }

    public sealed class VerifyInstanceFilesRequest
    {
        public InstanceId InstanceId { get; init; } = default!;
    }
}
