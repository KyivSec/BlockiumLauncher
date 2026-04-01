using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.UI.GtkSharp.Services;

public sealed class InstanceBrowserRefreshService
{
    public event EventHandler<InstanceBrowserRefreshEventArgs>? RefreshRequested;

    public void RequestRefresh(InstanceId? preferredInstanceId = null)
    {
        RefreshRequested?.Invoke(this, new InstanceBrowserRefreshEventArgs(preferredInstanceId));
    }
}

public sealed class InstanceBrowserRefreshEventArgs : EventArgs
{
    public InstanceBrowserRefreshEventArgs(InstanceId? preferredInstanceId)
    {
        PreferredInstanceId = preferredInstanceId;
    }

    public InstanceId? PreferredInstanceId { get; }
}
