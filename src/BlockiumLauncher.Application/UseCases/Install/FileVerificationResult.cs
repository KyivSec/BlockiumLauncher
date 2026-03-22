using System.Collections.Generic;
using BlockiumLauncher.Domain.Entities;

namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class FileVerificationResult
{
    public LauncherInstance Instance { get; init; } = default!;
    public bool IsValid { get; init; }
    public IReadOnlyList<FileVerificationIssue> Issues { get; init; } = [];
}