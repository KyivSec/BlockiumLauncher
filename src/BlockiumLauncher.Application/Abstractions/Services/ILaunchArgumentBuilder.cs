using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Services;

public interface ILaunchArgumentBuilder
{
    Task<Result<LaunchPlan>> BuildAsync(
        LauncherInstance Instance,
        LauncherAccount Account,
        JavaInstallation JavaInstallation,
        CancellationToken CancellationToken);
}
