namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class FileVerificationIssue
{
    public FileVerificationIssueKind Kind { get; init; }
    public string Path { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}