using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Launch;

public sealed class KillRunningInstanceRequest
{
    public InstanceId InstanceId { get; }

    public KillRunningInstanceRequest(InstanceId InstanceId)
    {
        this.InstanceId = InstanceId;
    }
}
