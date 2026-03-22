using System.Collections.Generic;
using BlockiumLauncher.Domain.Enums;

namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class InstallPlan
{
    public string InstanceName { get; init; } = string.Empty;
    public string GameVersion { get; init; } = string.Empty;
    public LoaderType LoaderType { get; init; }
    public string? LoaderVersion { get; init; }
    public string TargetDirectory { get; init; } = string.Empty;
    public bool DownloadRuntime { get; init; }
    public IReadOnlyList<InstallPlanStep> Steps { get; init; } = [];
}