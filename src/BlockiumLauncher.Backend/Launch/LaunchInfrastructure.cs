using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using BlockiumLauncher.Application.Abstractions.Launch;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Launch;
using BlockiumLauncher.Contracts.Launch;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Launch;

public sealed class InstanceLaunchSessionObserver : ILaunchSessionObserver
{
    private readonly IInstanceRepository instanceRepository;
    private readonly IInstanceContentMetadataService instanceContentMetadataService;

    public InstanceLaunchSessionObserver(
        IInstanceRepository instanceRepository,
        IInstanceContentMetadataService instanceContentMetadataService)
    {
        this.instanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
        this.instanceContentMetadataService = instanceContentMetadataService ?? throw new ArgumentNullException(nameof(instanceContentMetadataService));
    }

    public Task OnStartedAsync(Guid launchId, string instanceId, DateTimeOffset startedAtUtc, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task OnExitedAsync(
        Guid launchId,
        string instanceId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset exitedAtUtc,
        int? exitCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return;
        }

        var instance = await instanceRepository
            .GetByIdAsync(new InstanceId(instanceId), cancellationToken)
            .ConfigureAwait(false);

        if (instance is null)
        {
            return;
        }

        await instanceContentMetadataService
            .RecordLaunchAsync(instance, startedAtUtc, exitedAtUtc, cancellationToken)
            .ConfigureAwait(false);
    }
}

public sealed class JsonRuntimeMetadataStore : IRuntimeMetadataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<RuntimeMetadata?> LoadAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var runtimePath = Path.Combine(workingDirectory, ".blockium", "runtime.json");
        if (!File.Exists(runtimePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(runtimePath, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<RuntimeMetadata>(json, JsonOptions);
    }
}

public sealed class LaunchProcessRunner : ILaunchProcessRunner
{
    private readonly ConcurrentDictionary<Guid, LaunchSession> Sessions = new();
    private readonly ILaunchSessionObserver Observer;

    public LaunchProcessRunner()
        : this(NullLaunchSessionObserver.Instance)
    {
    }

    public LaunchProcessRunner(ILaunchSessionObserver Observer)
    {
        this.Observer = Observer ?? throw new ArgumentNullException(nameof(Observer));
    }

    public async Task<Result<LaunchInstanceResult>> StartAsync(LaunchPlanDto Plan, CancellationToken CancellationToken = default)
    {
        try
        {
            if (Plan is null || string.IsNullOrWhiteSpace(Plan.JavaExecutablePath))
            {
                return Result<LaunchInstanceResult>.Failure(LaunchErrors.ProcessStartFailed);
            }

            var JavaPath = NormalizeJavaExecutablePath(Plan.JavaExecutablePath);

            var StartInfo = new ProcessStartInfo
            {
                FileName = JavaPath,
                WorkingDirectory = Plan.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var Argument in Plan.JvmArguments)
            {
                StartInfo.ArgumentList.Add(Argument.Value);
            }

            StartInfo.ArgumentList.Add(Plan.MainClass);

            foreach (var Argument in Plan.GameArguments)
            {
                StartInfo.ArgumentList.Add(Argument.Value);
            }

            foreach (var Variable in Plan.EnvironmentVariables)
            {
                StartInfo.Environment[Variable.Name] = Variable.Value;
            }

            var Process = new Process
            {
                StartInfo = StartInfo,
                EnableRaisingEvents = true
            };

            var LaunchId = Guid.NewGuid();
            var Session = new LaunchSession(LaunchId, Plan, Process);

            if (!Sessions.TryAdd(LaunchId, Session))
            {
                return Result<LaunchInstanceResult>.Failure(LaunchErrors.ProcessStartFailed);
            }

            Process.OutputDataReceived += (_, Args) =>
            {
                if (Args.Data is not null)
                {
                    Session.AddOutput("stdout", Args.Data);
                }
            };

            Process.ErrorDataReceived += (_, Args) =>
            {
                if (Args.Data is not null)
                {
                    Session.AddOutput("stderr", Args.Data);
                }
            };

            Process.Exited += (_, _) =>
            {
                Session.MarkExitedSafe();
                _ = Observer.OnExitedAsync(
                    Session.LaunchId,
                    Session.Plan.InstanceId,
                    Session.StartedAtUtc ?? DateTimeOffset.UtcNow,
                    Session.ExitedAtUtc ?? DateTimeOffset.UtcNow,
                    Session.ExitCode);
            };

            var Started = Process.Start();
            if (!Started)
            {
                Sessions.TryRemove(LaunchId, out _);
                Process.Dispose();
                return Result<LaunchInstanceResult>.Failure(LaunchErrors.ProcessStartFailed);
            }

            Session.MarkStarted(Process.Id);
            Process.BeginOutputReadLine();
            Process.BeginErrorReadLine();
            await Observer.OnStartedAsync(LaunchId, Plan.InstanceId, Session.StartedAtUtc ?? DateTimeOffset.UtcNow, CancellationToken).ConfigureAwait(false);

            return Result<LaunchInstanceResult>.Success(Session.ToResult());
        }
        catch
        {
            return Result<LaunchInstanceResult>.Failure(LaunchErrors.ProcessStartFailed);
        }
    }

    public Task<Result<LaunchInstanceResult>> GetStatusAsync(Guid LaunchId, CancellationToken CancellationToken = default)
    {
        if (!Sessions.TryGetValue(LaunchId, out var Session))
        {
            return Task.FromResult(Result<LaunchInstanceResult>.Failure(LaunchErrors.LaunchSessionNotFound));
        }

        Session.RefreshState();
        return Task.FromResult(Result<LaunchInstanceResult>.Success(Session.ToResult()));
    }

    public Task<Result> StopAsync(Guid LaunchId, CancellationToken CancellationToken = default)
    {
        try
        {
            if (!Sessions.TryGetValue(LaunchId, out var Session))
            {
                return Task.FromResult(Result.Failure(LaunchErrors.LaunchSessionNotFound));
            }

            Session.RefreshState();
            if (Session.HasExited)
            {
                return Task.FromResult(Result.Success());
            }

            var Process = Session.Process;
            if (Process is null)
            {
                return Task.FromResult(Result.Failure(LaunchErrors.StopFailed));
            }

            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                Process.WaitForExit(10000);
            }

            Session.MarkExitedSafe();
            return Task.FromResult(Result.Success());
        }
        catch
        {
            return Task.FromResult(Result.Failure(LaunchErrors.StopFailed));
        }
    }

    private static string NormalizeJavaExecutablePath(string JavaPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return JavaPath;
        }

        if (!JavaPath.EndsWith("javaw.exe", StringComparison.OrdinalIgnoreCase))
        {
            return JavaPath;
        }

        var ConsoleJavaPath = Path.Combine(Path.GetDirectoryName(JavaPath)!, "java.exe");
        return File.Exists(ConsoleJavaPath) ? ConsoleJavaPath : JavaPath;
    }

    private sealed class LaunchSession
    {
        private readonly object Sync = new();
        private readonly List<LaunchOutputLine> OutputLines = [];

        public Guid LaunchId { get; }
        public LaunchPlanDto Plan { get; }
        public Process Process { get; }
        public int? ProcessId { get; private set; }
        public DateTimeOffset? StartedAtUtc { get; private set; }
        public DateTimeOffset? ExitedAtUtc { get; private set; }
        public bool IsRunning { get; private set; }
        public bool HasExited { get; private set; }
        public int? ExitCode { get; private set; }

        public LaunchSession(Guid LaunchId, LaunchPlanDto Plan, Process Process)
        {
            this.LaunchId = LaunchId;
            this.Plan = Plan;
            this.Process = Process;
        }

        public void MarkStarted(int ProcessId)
        {
            lock (Sync)
            {
                this.ProcessId = ProcessId;
                StartedAtUtc = DateTimeOffset.UtcNow;
                IsRunning = true;
                HasExited = false;
            }
        }

        public void AddOutput(string Stream, string Message)
        {
            lock (Sync)
            {
                OutputLines.Add(new LaunchOutputLine
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Stream = Stream,
                    Message = Message
                });
            }
        }

        public IReadOnlyList<LaunchOutputLine> GetOutputLines()
        {
            lock (Sync)
            {
                return OutputLines.ToList();
            }
        }

        public void RefreshState()
        {
            try
            {
                if (Process.HasExited)
                {
                    MarkExitedSafe();
                }
            }
            catch
            {
            }
        }

        public void MarkExitedSafe()
        {
            lock (Sync)
            {
                int? ExitCodeValue = ExitCode;

                try
                {
                    if (Process.HasExited)
                    {
                        ExitCodeValue = Process.ExitCode;
                    }
                }
                catch
                {
                }

                ExitCode = ExitCodeValue;
                ExitedAtUtc = DateTimeOffset.UtcNow;
                IsRunning = false;
                HasExited = true;
            }
        }

        public LaunchInstanceResult ToResult()
        {
            lock (Sync)
            {
                return new LaunchInstanceResult
                {
                    LaunchId = LaunchId,
                    InstanceId = Plan.InstanceId,
                    ProcessId = ProcessId,
                    IsRunning = IsRunning,
                    HasExited = HasExited,
                    ExitCode = ExitCode,
                    OutputLines = OutputLines.ToList(),
                    Plan = Plan
                };
            }
        }
    }
}
