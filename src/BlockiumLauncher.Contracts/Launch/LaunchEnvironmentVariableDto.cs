namespace BlockiumLauncher.Contracts.Launch;

public sealed class LaunchEnvironmentVariableDto
{
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}