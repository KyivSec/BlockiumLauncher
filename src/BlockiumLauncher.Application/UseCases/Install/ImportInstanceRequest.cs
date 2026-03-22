namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class ImportInstanceRequest
{
    public string SourceDirectory { get; init; } = string.Empty;

    public string InstanceName { get; init; } = string.Empty;

    public string? TargetDirectory { get; init; }

    public bool CopyInsteadOfMove { get; init; } = true;
}