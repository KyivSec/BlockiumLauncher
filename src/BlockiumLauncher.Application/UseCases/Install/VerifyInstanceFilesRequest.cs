using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class VerifyInstanceFilesRequest
{
    public InstanceId InstanceId { get; }
    public bool RepairOnMismatch { get; }

    public VerifyInstanceFilesRequest(InstanceId InstanceId, bool RepairOnMismatch = false)
    {
        this.InstanceId = InstanceId;
        this.RepairOnMismatch = RepairOnMismatch;
    }
}
