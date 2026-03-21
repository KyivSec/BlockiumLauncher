using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Services;

public interface ILoaderInstaller
{
    Task<Result> InstallAsync(LauncherInstance Instance, CancellationToken CancellationToken);
}
