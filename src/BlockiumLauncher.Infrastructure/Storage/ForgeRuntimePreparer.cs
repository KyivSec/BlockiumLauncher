using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Storage;

public sealed class ForgeRuntimePreparer : ILoaderRuntimePreparer
{
    private readonly IForgeInstallOrchestrator Orchestrator;

    public ForgeRuntimePreparer(IForgeInstallOrchestrator orchestrator)
    {
        Orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    public bool CanPrepare(LoaderType loaderType)
    {
        return loaderType == LoaderType.Forge;
    }

    public Task<Result<string>> PrepareAsync(
        InstallPlan plan,
        ITempWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        return Orchestrator.PrepareAsync(plan, workspace, cancellationToken);
    }
}
