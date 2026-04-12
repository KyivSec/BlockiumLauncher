using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Storage;

public interface ILoaderRuntimePreparer
{
    bool CanPrepare(LoaderType loaderType);

    Task<Result<string>> PrepareAsync(
        InstallPlan plan,
        ITempWorkspace workspace,
        IProgress<InstallPreparationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class FabricRuntimePreparer : ILoaderRuntimePreparer
{
    private readonly IFabricInstallOrchestrator Orchestrator;

    public FabricRuntimePreparer(IFabricInstallOrchestrator orchestrator)
    {
        Orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    public bool CanPrepare(LoaderType loaderType)
    {
        return loaderType == LoaderType.Fabric;
    }

    public Task<Result<string>> PrepareAsync(
        InstallPlan plan,
        ITempWorkspace workspace,
        IProgress<InstallPreparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Orchestrator.PrepareAsync(plan, workspace, progress, cancellationToken);
    }
}

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
        IProgress<InstallPreparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Orchestrator.PrepareAsync(plan, workspace, progress, cancellationToken);
    }
}

public sealed class NeoForgeRuntimePreparer : ILoaderRuntimePreparer
{
    private readonly INeoForgeInstallOrchestrator Orchestrator;

    public NeoForgeRuntimePreparer(INeoForgeInstallOrchestrator orchestrator)
    {
        Orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    public bool CanPrepare(LoaderType loaderType)
    {
        return loaderType == LoaderType.NeoForge;
    }

    public Task<Result<string>> PrepareAsync(
        InstallPlan plan,
        ITempWorkspace workspace,
        IProgress<InstallPreparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Orchestrator.PrepareAsync(plan, workspace, progress, cancellationToken);
    }
}

public sealed class QuiltRuntimePreparer : ILoaderRuntimePreparer
{
    private readonly IQuiltInstallOrchestrator Orchestrator;

    public QuiltRuntimePreparer(IQuiltInstallOrchestrator orchestrator)
    {
        Orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    public bool CanPrepare(LoaderType loaderType)
    {
        return loaderType == LoaderType.Quilt;
    }

    public Task<Result<string>> PrepareAsync(
        InstallPlan plan,
        ITempWorkspace workspace,
        IProgress<InstallPreparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Orchestrator.PrepareAsync(plan, workspace, progress, cancellationToken);
    }
}
