namespace BlockiumLauncher.Application.Abstractions.Diagnostics;

public interface IStructuredLogger
{
    void Info(OperationContext Context, string Source, string EventName, string Message, object? Data = null);
    void Warning(OperationContext Context, string Source, string EventName, string Message, object? Data = null);
    void Error(OperationContext Context, string Source, string EventName, string Message, object? Data = null, Exception? Exception = null);
}