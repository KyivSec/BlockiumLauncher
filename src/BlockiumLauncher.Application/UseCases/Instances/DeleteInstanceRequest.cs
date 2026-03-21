using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Instances;

public sealed class DeleteInstanceRequest
{
    public InstanceId InstanceId { get; }

    public DeleteInstanceRequest(InstanceId InstanceId)
    {
        this.InstanceId = InstanceId;
    }
}
