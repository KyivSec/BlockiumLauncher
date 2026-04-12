using BlockiumLauncher.Application.UseCases.Launch;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BlockiumLauncher.UI.GtkSharp.Services;

public sealed class LaunchController : IDisposable
{
    private readonly LaunchInstanceUseCase launchInstanceUseCase;
    private readonly StopLaunchUseCase stopLaunchUseCase;
    private readonly GetLaunchStatusUseCase getLaunchStatusUseCase;
    private readonly Dictionary<InstanceId, Guid> activeLaunches = new();
    private readonly Dictionary<InstanceId, LaunchInstanceResult> lastStatuses = new();
    private uint? timeoutId;
    private bool disposed;

    public event EventHandler<InstanceId>? StatusChanged;

    public LaunchController(
        LaunchInstanceUseCase launchInstanceUseCase,
        StopLaunchUseCase stopLaunchUseCase,
        GetLaunchStatusUseCase getLaunchStatusUseCase)
    {
        this.launchInstanceUseCase = launchInstanceUseCase ?? throw new ArgumentNullException(nameof(launchInstanceUseCase));
        this.stopLaunchUseCase = stopLaunchUseCase ?? throw new ArgumentNullException(nameof(stopLaunchUseCase));
        this.getLaunchStatusUseCase = getLaunchStatusUseCase ?? throw new ArgumentNullException(nameof(getLaunchStatusUseCase));
    }

    public bool IsRunning(InstanceId instanceId)
    {
        lock (lastStatuses)
        {
            return lastStatuses.TryGetValue(instanceId, out var status) && status.IsRunning;
        }
    }

    public string? GetStatusMessage(InstanceId instanceId)
    {
        lock (lastStatuses)
        {
            if (lastStatuses.TryGetValue(instanceId, out var status))
            {
                if (status.IsRunning) return "Running";
                if (status.HasExited) return $"Exited (Code: {status.ExitCode})";
            }
            return null;
        }
    }

    public async Task<Result<LaunchInstanceResult>> StartLaunchAsync(InstanceId instanceId)
    {
        var request = new LaunchInstanceRequest
        {
            InstanceId = instanceId
        };

        var result = await launchInstanceUseCase.ExecuteAsync(request);
        if (result.IsSuccess)
        {
            lock (activeLaunches)
            {
                activeLaunches[instanceId] = result.Value.LaunchId;
            }
            lock (lastStatuses)
            {
                lastStatuses[instanceId] = result.Value;
            }
            EnsurePollingStarted();
            RaiseStatusChanged(instanceId);
        }

        return result;
    }

    public async Task<Result> StopLaunchAsync(InstanceId instanceId)
    {
        Guid launchId;
        lock (activeLaunches)
        {
            if (!activeLaunches.TryGetValue(instanceId, out launchId))
            {
                return Result.Success();
            }
        }

        var result = await stopLaunchUseCase.ExecuteAsync(new StopLaunchRequest { LaunchId = launchId });
        if (result.IsSuccess)
        {
            // The poller will clean it up on next tick, but we can proactively trigger a change
            RaiseStatusChanged(instanceId);
        }
        return result;
    }

    private bool OnPollStatus()
    {
        if (disposed)
        {
            return false;
        }

        _ = PollInternalAsync();
        return true; // Keep timeout active
    }

    private async Task PollInternalAsync()
    {
        if (disposed)
        {
            return;
        }

        List<KeyValuePair<InstanceId, Guid>> currentLaunches;
        lock (activeLaunches)
        {
            currentLaunches = [.. activeLaunches];
        }

        if (currentLaunches.Count == 0)
        {
            StopPolling();
            return;
        }

        foreach (var pair in currentLaunches)
        {
            var result = await getLaunchStatusUseCase.ExecuteAsync(new GetLaunchStatusRequest { LaunchId = pair.Value });
            if (result.IsSuccess)
            {
                bool changed = false;
                lock (lastStatuses)
                {
                    if (!lastStatuses.TryGetValue(pair.Key, out var oldStatus) || 
                        oldStatus.IsRunning != result.Value.IsRunning || 
                        oldStatus.HasExited != result.Value.HasExited)
                    {
                        lastStatuses[pair.Key] = result.Value;
                        changed = true;
                    }
                }

                if (result.Value.HasExited)
                {
                    lock (activeLaunches)
                    {
                        activeLaunches.Remove(pair.Key);
                    }
                    EnsurePollingState();
                }

                if (changed)
                {
                    RaiseStatusChanged(pair.Key);
                }
            }
            else
            {
                // If we can't find the status, assume it's gone
                lock (activeLaunches)
                {
                    activeLaunches.Remove(pair.Key);
                }
                EnsurePollingState();
                lock (lastStatuses)
                {
                    lastStatuses.Remove(pair.Key);
                }
                RaiseStatusChanged(pair.Key);
            }
        }
    }

    private void RaiseStatusChanged(InstanceId instanceId)
    {
        if (disposed)
        {
            return;
        }

        Gtk.Application.Invoke((_, _) =>
        {
            if (disposed)
            {
                return;
            }

            StatusChanged?.Invoke(this, instanceId);
        });
    }

    private void EnsurePollingStarted()
    {
        if (disposed || timeoutId.HasValue)
        {
            return;
        }

        timeoutId = GLib.Timeout.Add(1000, OnPollStatus);
    }

    private void EnsurePollingState()
    {
        lock (activeLaunches)
        {
            if (activeLaunches.Count == 0)
            {
                StopPolling();
            }
        }
    }

    private void StopPolling()
    {
        if (!timeoutId.HasValue)
        {
            return;
        }

        GLib.Source.Remove(timeoutId.Value);
        timeoutId = null;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        StopPolling();
    }
}
