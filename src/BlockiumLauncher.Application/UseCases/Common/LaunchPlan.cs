namespace BlockiumLauncher.Application.UseCases.Common;

public sealed class LaunchPlan
{
    public string ExecutablePath { get; }
    public string WorkingDirectory { get; }
    public IReadOnlyList<string> Arguments { get; }
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; }

    public LaunchPlan(
        string ExecutablePath,
        string WorkingDirectory,
        IReadOnlyList<string> Arguments,
        IReadOnlyDictionary<string, string> EnvironmentVariables)
    {
        this.ExecutablePath = NormalizeRequired(ExecutablePath, nameof(ExecutablePath));
        this.WorkingDirectory = NormalizeRequired(WorkingDirectory, nameof(WorkingDirectory));
        this.Arguments = Arguments ?? throw new ArgumentNullException(nameof(Arguments));
        this.EnvironmentVariables = EnvironmentVariables ?? throw new ArgumentNullException(nameof(EnvironmentVariables));
    }

    private static string NormalizeRequired(string Value, string ParamName)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", ParamName);
        }

        return Value.Trim();
    }
}
