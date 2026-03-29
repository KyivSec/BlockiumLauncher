using System.Text.RegularExpressions;
using BlockiumLauncher.Application.Abstractions.Diagnostics;
using BlockiumLauncher.Infrastructure.Persistence.Paths;

namespace BlockiumLauncher.Infrastructure.Diagnostics;

public sealed class FileStructuredLogger : IStructuredLogger
{
    private readonly ISecretRedactor SecretRedactor;
    private readonly ILauncherPaths LauncherPaths;
    private readonly object Sync = new();

    public FileStructuredLogger(ISecretRedactor SecretRedactor, ILauncherPaths launcherPaths)
    {
        this.SecretRedactor = SecretRedactor ?? throw new ArgumentNullException(nameof(SecretRedactor));
        LauncherPaths = launcherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
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
            var TimestampUtc = DateTimeOffset.UtcNow;
            Directory.CreateDirectory(LauncherPaths.LogsDirectory);

            var Line = FormatLine(TimestampUtc, Level, Context, Source, EventName, Message, Data, Exception);
            var ContextLogPath = LauncherPaths.GetContextLogFilePath(Context.OperationName, TimestampUtc);

            lock (Sync)
            {
                File.AppendAllText(ContextLogPath, Line + Environment.NewLine);
                File.AppendAllText(LauncherPaths.LatestLogFilePath, Line + Environment.NewLine);
            }
        }
        catch
        {
        }
    }

    private string FormatLine(
        DateTimeOffset timestampUtc,
        string level,
        OperationContext context,
        string source,
        string eventName,
        string message,
        object? data,
        Exception? exception)
    {
        var parts = new List<string>
        {
            $"[{timestampUtc:yyyy-MM-dd HH:mm:ss.fff 'UTC'}]",
            level,
            $"op={context.OperationName}",
            $"opId={context.OperationId}",
            $"src={source}",
            $"event={eventName}",
            message
        };

        if (data is not null)
        {
            parts.Add($"data={data}");
        }

        if (exception is not null)
        {
            parts.Add($"exception={exception.GetType().FullName ?? exception.GetType().Name}");
            parts.Add($"details={exception.Message}");
        }

        return SecretRedactor.Redact(string.Join(" | ", parts));
    }
}

public sealed class SensitiveDataRedactor : ISecretRedactor
{
    private static readonly Regex JsonSecretPattern = new(
        @"(?i)(""(?:(?:access|refresh|id)?token|authorization|password|secret|clientsecret)""\s*:\s*"")([^""]+)("")",
        RegexOptions.Compiled);

    private static readonly Regex BearerPattern = new(
        "(?i)Bearer\\s+[A-Za-z0-9\\-\\._~\\+\\/]+=*",
        RegexOptions.Compiled);

    public string Redact(string Value)
    {
        if (string.IsNullOrEmpty(Value))
        {
            return Value;
        }

        var Redacted = JsonSecretPattern.Replace(Value, "$1***REDACTED***$3");
        Redacted = BearerPattern.Replace(Redacted, "Bearer ***REDACTED***");
        return Redacted;
    }
}
