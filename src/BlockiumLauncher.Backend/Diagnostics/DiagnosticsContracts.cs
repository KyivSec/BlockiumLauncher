namespace BlockiumLauncher.Application.Abstractions.Diagnostics;

public interface IOperationContextFactory
{
    OperationContext Create(string OperationName);
}

public interface ISecretRedactor
{
    string Redact(string Value);
}

public interface IStructuredLogger
{
    void Info(OperationContext Context, string Source, string EventName, string Message, object? Data = null);
    void Warning(OperationContext Context, string Source, string EventName, string Message, object? Data = null);
    void Error(OperationContext Context, string Source, string EventName, string Message, object? Data = null, Exception? Exception = null);
}

public sealed class OperationContext
{
    public string OperationId { get; init; } = Guid.NewGuid().ToString("N");
    public string OperationName { get; init; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
