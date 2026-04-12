using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Application.UseCases.Launch;
using BlockiumLauncher.Contracts.Launch;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Launch
{
    public interface ILaunchProcessRunner
    {
        Task<Result<LaunchInstanceResult>> StartAsync(LaunchPlanDto Plan, CancellationToken CancellationToken = default);
        Task<Result<LaunchInstanceResult>> GetStatusAsync(Guid LaunchId, CancellationToken CancellationToken = default);
        Task<Result> StopAsync(Guid LaunchId, CancellationToken CancellationToken = default);
        Task<Result<IReadOnlyList<LaunchOutputLine>>> GetLatestOutputAsync(string instanceId, CancellationToken CancellationToken = default);
        Task<Result> ClearLatestOutputAsync(string instanceId, CancellationToken CancellationToken = default);
    }

    public interface ILaunchSessionObserver
    {
        Task OnStartedAsync(
            Guid launchId,
            string instanceId,
            DateTimeOffset startedAtUtc,
            CancellationToken cancellationToken = default);

        Task OnExitedAsync(
            Guid launchId,
            string instanceId,
            DateTimeOffset startedAtUtc,
            DateTimeOffset exitedAtUtc,
            int? exitCode,
            CancellationToken cancellationToken = default);
    }

    public interface IRuntimeMetadataStore
    {
        Task<RuntimeMetadata?> LoadAsync(string workingDirectory, CancellationToken cancellationToken = default);
    }

    public sealed class RuntimeMetadata
    {
        public string Version { get; init; } = string.Empty;
        public string MainClass { get; init; } = string.Empty;
        public string ClientJarPath { get; init; } = string.Empty;
        public IReadOnlyList<string> ClasspathEntries { get; init; } = [];
        public string AssetsDirectory { get; init; } = string.Empty;
        public string AssetIndexId { get; init; } = string.Empty;
        public string NativesDirectory { get; init; } = string.Empty;
        public string LibraryDirectory { get; init; } = string.Empty;
        public IReadOnlyList<string> ExtraJvmArguments { get; init; } = [];
        public IReadOnlyList<string> ExtraGameArguments { get; init; } = [];
    }
}

namespace BlockiumLauncher.Application.Abstractions.Services
{
    public interface IGameProcessLauncher
    {
        Task<Result<ProcessLaunchResult>> LaunchAsync(LaunchPlan LaunchPlan, CancellationToken CancellationToken);
        Task<Result> KillAsync(int ProcessId, CancellationToken CancellationToken);
    }

    public interface ILaunchArgumentBuilder
    {
        Task<Result<LaunchPlan>> BuildAsync(
            LauncherInstance Instance,
            LauncherAccount Account,
            JavaInstallation JavaInstallation,
            CancellationToken CancellationToken);
    }
}
