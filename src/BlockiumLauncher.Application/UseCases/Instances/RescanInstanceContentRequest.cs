using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Instances;

public sealed class RescanInstanceContentRequest
{
    public InstanceId InstanceId { get; init; }
}
