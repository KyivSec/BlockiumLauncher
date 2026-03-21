using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class UpdateInstanceRequest
{
    public InstanceId InstanceId { get; }
    public bool ForceMetadataRefresh { get; }

    public UpdateInstanceRequest(InstanceId InstanceId, bool ForceMetadataRefresh = false)
    {
        this.InstanceId = InstanceId;
        this.ForceMetadataRefresh = ForceMetadataRefresh;
    }
}
