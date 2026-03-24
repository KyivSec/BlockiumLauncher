using System.Text.Json;
using BlockiumLauncher.Application.Abstractions.Diagnostics;

namespace BlockiumLauncher.Infrastructure.Diagnostics;

public sealed class FileStructuredLogger : IStructuredLogger
{
    private readonly ISecretRedactor SecretRedactor;
    private readonly string LogsDirectory;
    private readonly object Sync = new();

    public FileStructuredLogger(ISecretRedactor SecretRedactor)
    {
        this.SecretRedactor = SecretRedactor ?? throw new ArgumentNullException(nameof(SecretRedactor));
        LogsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BlockiumLauncher",
            "logs");
    }

    public void Info(OperationContext Context, string Source, string EventName, string Message, object? Data = null)
    {
        Write("Information", Context, Source, EventName, Message, Data, null);
    }

    public void Warning(OperationContext Context, string Source, string EventName, string Message, object? Data = null)
    {
        Write("Warning", Context, Source, EventName, Message, Data, null);
    }

    public void Error(OperationContext Context, string Source, string EventName, string Message, object? Data = null, Exception? Exception = null)
    {
        Write("Error", Context, Source, EventName, Message, Data, Exception);
    }

    private void Write(
        string Level,
        OperationContext Context,
        string Source,
        string EventName,
        string Message,
        object? Data,
        Exception? Exception)
    {
        try
        {
            Directory.CreateDirectory(LogsDirectory);

            var Payload = new
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Level,
                Context.OperationId,
                Context.OperationName,
                Source,
                EventName,
                Message,
                Data,
                Exception = Exception is null ? null : new
                {
                    Type = Exception.GetType().FullName,
                    Exception.Message,
                    Exception.StackTrace
                }
            };

            var Json = JsonSerializer.Serialize(Payload);
            Json = SecretRedactor.Redact(Json);

            var FilePath = Path.Combine(LogsDirectory, DateTime.UtcNow.ToString("yyyyMMdd") + ".jsonl");

            lock (Sync)
            {
                File.AppendAllText(FilePath, Json + Environment.NewLine);
            }
        }
        catch
        {
        }
    }
}