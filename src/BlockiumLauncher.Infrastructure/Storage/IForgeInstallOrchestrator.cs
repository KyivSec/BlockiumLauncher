using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Storage;

public interface IForgeInstallOrchestrator
{
    Task<Result<string>> PrepareAsync(
        InstallPlan plan,
        ITempWorkspace workspace,
        CancellationToken cancellationToken = default);
}
