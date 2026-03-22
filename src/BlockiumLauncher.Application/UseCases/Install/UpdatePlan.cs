using System.Collections.Generic;
using BlockiumLauncher.Domain.Entities;

namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class UpdatePlan
{
    public LauncherInstance Instance { get; init; } = default!;
    public bool IsNoOp { get; init; }
    public bool RequiresRepair { get; init; }
    public IReadOnlyList<UpdatePlanStep> Steps { get; init; } = [];
}