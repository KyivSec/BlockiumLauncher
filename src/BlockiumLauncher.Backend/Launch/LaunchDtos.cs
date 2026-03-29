namespace BlockiumLauncher.Contracts.Launch;

public sealed class LaunchArgumentDto
{
    public string Value { get; init; } = string.Empty;
}

public sealed class LaunchEnvironmentVariableDto
{
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

public sealed class LaunchPlanDto
{
    public string InstanceId { get; init; } = string.Empty;
    public string AccountId { get; init; } = string.Empty;
    public string JavaExecutablePath { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public string MainClass { get; init; } = string.Empty;
    public string? AssetsDirectory { get; init; }
    public string? AssetIndexId { get; init; }
    public IReadOnlyList<string> ClasspathEntries { get; init; } = [];
    public IReadOnlyList<LaunchArgumentDto> JvmArguments { get; init; } = [];
    public IReadOnlyList<LaunchArgumentDto> GameArguments { get; init; } = [];
    public IReadOnlyList<LaunchEnvironmentVariableDto> EnvironmentVariables { get; init; } = [];
    public bool IsDryRun { get; init; }
}
