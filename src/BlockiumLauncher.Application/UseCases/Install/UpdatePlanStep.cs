namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class UpdatePlanStep
{
    public UpdatePlanStepKind Kind { get; init; }
    public string Message { get; init; } = string.Empty;
}