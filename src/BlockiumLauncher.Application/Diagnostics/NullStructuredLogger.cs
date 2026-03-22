using BlockiumLauncher.Application.Abstractions.Diagnostics;

namespace BlockiumLauncher.Application.Diagnostics;

public sealed class NullStructuredLogger : IStructuredLogger
{
    public static readonly NullStructuredLogger Instance = new();

    public void Info(OperationContext Context, string Source, string EventName, string Message, object? Data = null)
    {
    }

    public void Warning(OperationContext Context, string Source, string EventName, string Message, object? Data = null)
    {
    }

    public void Error(OperationContext Context, string Source, string EventName, string Message, object? Data = null, Exception? Exception = null)
    {
    }
}