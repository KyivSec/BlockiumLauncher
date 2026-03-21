using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Instances;

public sealed class GetInstanceDetailsRequest
{
    public InstanceId InstanceId { get; }

    public GetInstanceDetailsRequest(InstanceId InstanceId)
    {
        this.InstanceId = InstanceId;
    }
}
