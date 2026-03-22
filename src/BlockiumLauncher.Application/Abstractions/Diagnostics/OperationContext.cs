namespace BlockiumLauncher.Application.Abstractions.Diagnostics;

public sealed class OperationContext
{
    public string OperationId { get; init; } = Guid.NewGuid().ToString("N");
    public string OperationName { get; init; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}