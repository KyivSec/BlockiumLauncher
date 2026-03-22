using BlockiumLauncher.Domain.Entities;

namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class InstallInstanceResult
{
    public LauncherInstance Instance { get; init; } = default!;

    public string InstalledPath { get; init; } = string.Empty;
}