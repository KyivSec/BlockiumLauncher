using System.Collections.Generic;
using BlockiumLauncher.Domain.Entities;

namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class RepairInstanceResult
{
    public LauncherInstance Instance { get; init; } = default!;
    public bool Changed { get; init; }
    public IReadOnlyList<string> RepairedPaths { get; init; } = [];
    public FileVerificationResult Verification { get; init; } = default!;
}