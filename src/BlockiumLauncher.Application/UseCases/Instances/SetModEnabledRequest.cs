using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Instances;

public sealed class SetModEnabledRequest
{
    public InstanceId InstanceId { get; init; }
    public string ModReference { get; init; } = string.Empty;
    public bool Enabled { get; init; }
}
