using BlockiumLauncher.Application.Abstractions.Diagnostics;

namespace BlockiumLauncher.Application.Diagnostics;

public sealed class DefaultOperationContextFactory : IOperationContextFactory
{
    public static readonly DefaultOperationContextFactory Instance = new();

    public OperationContext Create(string OperationName)
    {
        return new OperationContext
        {
            OperationId = Guid.NewGuid().ToString("N"),
            OperationName = OperationName,
            StartedAtUtc = DateTimeOffset.UtcNow
        };
    }
}