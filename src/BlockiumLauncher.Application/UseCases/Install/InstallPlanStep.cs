namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class InstallPlanStep
{
    public InstallPlanStepKind Kind { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Destination { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public enum InstallPlanStepKind
{
    CreateDirectory = 0,
    ExtractArchive = 1,
    WriteMetadata = 2
}