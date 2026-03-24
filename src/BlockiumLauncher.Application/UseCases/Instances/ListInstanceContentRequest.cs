using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Instances;

public sealed class ListInstanceContentRequest
{
    public InstanceId InstanceId { get; init; }
}
