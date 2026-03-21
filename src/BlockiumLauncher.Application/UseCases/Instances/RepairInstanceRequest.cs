using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Instances;

public sealed class RepairInstanceRequest
{
    public InstanceId InstanceId { get; }
    public bool ForceFullRepair { get; }

    public RepairInstanceRequest(InstanceId InstanceId, bool ForceFullRepair = false)
    {
        this.InstanceId = InstanceId;
        this.ForceFullRepair = ForceFullRepair;
    }
}
