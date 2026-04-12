using BlockiumLauncher.Application.Abstractions.Launch;
using BlockiumLauncher.Contracts.Launch;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Errors;

namespace BlockiumLauncher.Application.UseCases.Launch
{
    public sealed class BuildLaunchPlanRequest
    {
        public InstanceId InstanceId { get; init; }
        public AccountId? AccountId { get; init; }
        public string JavaExecutablePath { get; init; } = string.Empty;
        public string MainClass { get; init; } = string.Empty;
        public string? AssetsDirectory { get; init; }
        public string? AssetIndexId { get; init; }
        public IReadOnlyList<string> ClasspathEntries { get; init; } = [];
        public bool IsDryRun { get; init; } = true;
    }

    public sealed class GetLaunchStatusRequest
    {
        public Guid LaunchId { get; init; }
    }

    public sealed class KillRunningInstanceRequest
    {
        public InstanceId InstanceId { get; }

        public KillRunningInstanceRequest(InstanceId InstanceId)
        {
            this.InstanceId = InstanceId;
        }
    }

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

    public sealed class LaunchInstanceRequest
    {
        public InstanceId InstanceId { get; init; }
        public AccountId? AccountId { get; init; }
        public string JavaExecutablePath { get; init; } = string.Empty;
        public string MainClass { get; init; } = string.Empty;
        public string? AssetsDirectory { get; init; }
        public string? AssetIndexId { get; init; }
        public IReadOnlyList<string> ClasspathEntries { get; init; } = [];
    }

    public sealed class LaunchInstanceResult
    {
        public Guid LaunchId { get; init; }
        public string InstanceId { get; init; } = string.Empty;
        public int? ProcessId { get; init; }
        public bool IsRunning { get; init; }
        public bool HasExited { get; init; }
        public int? ExitCode { get; init; }
        public IReadOnlyList<LaunchOutputLine> OutputLines { get; init; } = [];
        public LaunchPlanDto Plan { get; init; } = default!;
    }

    public sealed class LaunchOutputLine
    {
        public DateTimeOffset TimestampUtc { get; init; }
        public string Stream { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }

    public sealed class NullLaunchSessionObserver : ILaunchSessionObserver
    {
        public static readonly NullLaunchSessionObserver Instance = new();

        private NullLaunchSessionObserver()
        {
        }

        public Task OnStartedAsync(Guid launchId, string instanceId, DateTimeOffset startedAtUtc, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task OnExitedAsync(Guid launchId, string instanceId, DateTimeOffset startedAtUtc, DateTimeOffset exitedAtUtc, int? exitCode, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    public sealed class NullRuntimeMetadataStore : IRuntimeMetadataStore
    {
        public static NullRuntimeMetadataStore Instance { get; } = new();

        private NullRuntimeMetadataStore()
        {
        }

        public Task<RuntimeMetadata?> LoadAsync(string workingDirectory, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<RuntimeMetadata?>(null);
        }
    }

    public sealed class StopLaunchRequest
    {
        public Guid LaunchId { get; init; }
    }

    public sealed class TailLogRequest
    {
        public InstanceId InstanceId { get; }
        public int MaxLines { get; }

        public TailLogRequest(InstanceId InstanceId, int MaxLines = 200)
        {
            if (MaxLines <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxLines), "MaxLines must be greater than zero.");
            }

            this.InstanceId = InstanceId;
            this.MaxLines = MaxLines;
        }
    }

    public sealed class GetLatestLaunchOutputRequest
    {
        public InstanceId InstanceId { get; init; }
    }

    public sealed class ClearLatestLaunchOutputRequest
    {
        public InstanceId InstanceId { get; init; }
    }
}
