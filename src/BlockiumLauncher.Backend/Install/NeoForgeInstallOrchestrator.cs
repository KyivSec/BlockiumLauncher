using BlockiumLauncher.Application.Abstractions.Diagnostics;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.Diagnostics;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Storage;

public sealed class NeoForgeInstallOrchestrator : INeoForgeInstallOrchestrator
{
    private readonly IStructuredLogger Logger;
    private readonly IOperationContextFactory OperationContextFactory;
    private readonly SharedRuntimeDownloadSupport SharedRuntimeDownloadSupport;
    private readonly InstalledLoaderRuntimeSupport InstalledLoaderRuntimeSupport;

    public NeoForgeInstallOrchestrator()
        : this(
            NullStructuredLogger.Instance,
            DefaultOperationContextFactory.Instance,
            NoOpJavaRuntimeResolver.Instance,
            LauncherPaths.CreateDefault())
    {
    }

    public NeoForgeInstallOrchestrator(
        IStructuredLogger Logger,
        IOperationContextFactory OperationContextFactory,
        IJavaRuntimeResolver JavaRuntimeResolver,
        ILauncherPaths LauncherPaths)
    {
        var ConcretePaths = LauncherPaths as LauncherPaths ?? throw new ArgumentNullException(nameof(LauncherPaths));

        this.Logger = Logger ?? throw new ArgumentNullException(nameof(Logger));
        this.OperationContextFactory = OperationContextFactory ?? throw new ArgumentNullException(nameof(OperationContextFactory));
        SharedRuntimeDownloadSupport = new SharedRuntimeDownloadSupport(nameof(NeoForgeInstallOrchestrator), this.Logger, ConcretePaths);
        InstalledLoaderRuntimeSupport = new InstalledLoaderRuntimeSupport(nameof(NeoForgeInstallOrchestrator), this.Logger, JavaRuntimeResolver, ConcretePaths);
    }

    public async Task<Result<string>> PrepareAsync(
        InstallPlan Plan,
        ITempWorkspace Workspace,
        IProgress<InstallPreparationProgress>? Progress = null,
        CancellationToken CancellationToken = default)
    {
        var Context = OperationContextFactory.Create("PrepareInstanceContent");

        try
        {
            CancellationToken.ThrowIfCancellationRequested();

            Progress?.Report(new InstallPreparationProgress(
                InstallPreparationPhase.Preparing,
                "Preparing instance runtime",
                "Resolving the staged instance layout and runtime content."));

            Logger.Info(Context, nameof(NeoForgeInstallOrchestrator), "PrepareStarted", "Preparing instance content.", new
            {
                Plan.InstanceName,
                Plan.GameVersion,
                LoaderType = Plan.LoaderType.ToString(),
                Plan.LoaderVersion,
                Plan.DownloadRuntime
            });

            var RootPath = await RuntimeWorkspaceSupport.PrepareRootAsync(Plan, Workspace, CancellationToken).ConfigureAwait(false);

            Logger.Info(Context, nameof(NeoForgeInstallOrchestrator), "WorkspaceResolved", "Resolved temporary instance root.", new
            {
                RootPath
            });

            if (Plan.DownloadRuntime)
            {
                await PrepareRuntimeAsync(Plan, RootPath, Context, Progress, CancellationToken).ConfigureAwait(false);
            }

            await RuntimeWorkspaceSupport.WriteInstanceMarkerAsync(Plan, RootPath, CancellationToken).ConfigureAwait(false);
            return Result<string>.Success(RootPath);
        }
        catch (Exception Exception)
        {
            Logger.Error(Context, nameof(NeoForgeInstallOrchestrator), "PrepareFailed", "Instance content preparation failed.", new
            {
                Plan.InstanceName,
                Plan.GameVersion,
                LoaderType = Plan.LoaderType.ToString(),
                Plan.LoaderVersion
            }, Exception);

            return Result<string>.Failure(InstallErrors.DownloadFailed);
        }
    }

    private async Task PrepareRuntimeAsync(
        InstallPlan Plan,
        string RootPath,
        OperationContext Context,
        IProgress<InstallPreparationProgress>? Progress,
        CancellationToken CancellationToken)
    {
        switch (Plan.LoaderType)
        {
            case LoaderType.Vanilla:
                await SharedRuntimeDownloadSupport.DownloadVanillaRuntimeAsync(Plan, RootPath, Context, Progress, CancellationToken).ConfigureAwait(false);
                break;

            case LoaderType.Fabric:
                if (string.IsNullOrWhiteSpace(Plan.LoaderVersion))
                {
                    throw new InvalidOperationException("Fabric preparation requires a loader version.");
                }

                await SharedRuntimeDownloadSupport.DownloadVanillaRuntimeAsync(Plan, RootPath, Context, Progress, CancellationToken).ConfigureAwait(false);
                Progress?.Report(new InstallPreparationProgress(
                    InstallPreparationPhase.ApplyingLoaderProfile,
                    "Preparing loader runtime",
                    "Applying the Fabric loader profile."));
                await SharedRuntimeDownloadSupport.ApplyFabricRuntimeAsync(Plan, RootPath, CancellationToken).ConfigureAwait(false);
                break;

            case LoaderType.NeoForge:
                if (string.IsNullOrWhiteSpace(Plan.LoaderVersion))
                {
                    throw new InvalidOperationException("NeoForge preparation requires a loader version.");
                }

                await SharedRuntimeDownloadSupport.DownloadVanillaRuntimeAsync(Plan, RootPath, Context, Progress, CancellationToken).ConfigureAwait(false);

                Progress?.Report(new InstallPreparationProgress(
                    InstallPreparationPhase.ApplyingLoaderProfile,
                    "Preparing loader runtime",
                    "Installing the NeoForge runtime."));

                var RuntimeRoot = await InstalledLoaderRuntimeSupport.DownloadAndInstallAsync(
                    Plan,
                    LoaderType.NeoForge,
                    $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{Plan.LoaderVersion}/neoforge-{Plan.LoaderVersion}-installer.jar",
                    $"neoforge-{Plan.LoaderVersion}-installer.jar",
                    "--install-client",
                    Context,
                    CancellationToken).ConfigureAwait(false);

                await InstalledLoaderRuntimeSupport.BuildInstalledRuntimeMetadataAsync(
                    Plan,
                    LoaderType.NeoForge,
                    RootPath,
                    RuntimeRoot,
                    "neoforge-version.json",
                    Context,
                    CancellationToken).ConfigureAwait(false);
                break;

            default:
                throw new InvalidOperationException($"Unsupported loader type '{Plan.LoaderType}'.");
        }
    }
}
