using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class RepairInstanceRequest
{
    public InstanceId InstanceId { get; init; } = default!;
}