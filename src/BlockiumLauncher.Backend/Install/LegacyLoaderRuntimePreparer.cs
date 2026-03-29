using BlockiumLauncher.Application.Abstractions.Diagnostics;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.Diagnostics;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Storage;

public sealed class LegacyLoaderRuntimePreparer : ILoaderRuntimePreparer
{
    private readonly IStructuredLogger Logger;
    private readonly IOperationContextFactory OperationContextFactory;
    private readonly SharedRuntimeDownloadSupport SharedRuntimeDownloadSupport;
    private readonly InstalledLoaderRuntimeSupport InstalledLoaderRuntimeSupport;

    public LegacyLoaderRuntimePreparer()
        : this(
            NullStructuredLogger.Instance,
            DefaultOperationContextFactory.Instance,
            NoOpJavaRuntimeResolver.Instance,
            LauncherPaths.CreateDefault())
    {
    }

    public LegacyLoaderRuntimePreparer(
        IStructuredLogger Logger,
        IOperationContextFactory OperationContextFactory,
        IJavaRuntimeResolver JavaRuntimeResolver,
        ILauncherPaths LauncherPaths)
    {
        var ConcretePaths = LauncherPaths as LauncherPaths ?? throw new ArgumentNullException(nameof(LauncherPaths));

        this.Logger = Logger ?? throw new ArgumentNullException(nameof(Logger));
        this.OperationContextFactory = OperationContextFactory ?? throw new ArgumentNullException(nameof(OperationContextFactory));
        SharedRuntimeDownloadSupport = new SharedRuntimeDownloadSupport(nameof(LegacyLoaderRuntimePreparer), this.Logger, ConcretePaths);
        InstalledLoaderRuntimeSupport = new InstalledLoaderRuntimeSupport(nameof(LegacyLoaderRuntimePreparer), this.Logger, JavaRuntimeResolver, ConcretePaths);
    }

    public bool CanPrepare(LoaderType loaderType)
    {
        return loaderType == LoaderType.Vanilla;
    }

    public async Task<Result<string>> PrepareAsync(
        InstallPlan Plan,
        ITempWorkspace Workspace,
        CancellationToken CancellationToken = default)
    {
        var Context = OperationContextFactory.Create("PrepareInstanceContent");

        try
        {
            CancellationToken.ThrowIfCancellationRequested();

            Logger.Info(Context, nameof(LegacyLoaderRuntimePreparer), "PrepareStarted", "Preparing instance content.", new
            {
                Plan.InstanceName,
                Plan.GameVersion,
                LoaderType = Plan.LoaderType.ToString(),
                Plan.LoaderVersion,
                Plan.DownloadRuntime
            });

            var RootPath = await RuntimeWorkspaceSupport.PrepareRootAsync(Plan, Workspace, CancellationToken).ConfigureAwait(false);

            Logger.Info(Context, nameof(LegacyLoaderRuntimePreparer), "WorkspaceResolved", "Resolved temporary instance root.", new
            {
                RootPath
            });

            if (Plan.DownloadRuntime)
            {
                await PrepareRuntimeAsync(Plan, RootPath, Context, CancellationToken).ConfigureAwait(false);
            }

            await RuntimeWorkspaceSupport.WriteInstanceMarkerAsync(Plan, RootPath, CancellationToken).ConfigureAwait(false);
            return Result<string>.Success(RootPath);
        }
        catch (Exception Exception)
        {
            Logger.Error(Context, nameof(LegacyLoaderRuntimePreparer), "PrepareFailed", "Instance content preparation failed.", new
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
        CancellationToken CancellationToken)
    {
        switch (Plan.LoaderType)
        {
            case LoaderType.Vanilla:
                await SharedRuntimeDownloadSupport.DownloadVanillaRuntimeAsync(Plan, RootPath, Context, CancellationToken).ConfigureAwait(false);
                break;

            case LoaderType.Fabric:
                if (string.IsNullOrWhiteSpace(Plan.LoaderVersion))
                {
                    throw new InvalidOperationException("Fabric preparation requires a loader version.");
                }

                await SharedRuntimeDownloadSupport.DownloadVanillaRuntimeAsync(Plan, RootPath, Context, CancellationToken).ConfigureAwait(false);
                await SharedRuntimeDownloadSupport.ApplyFabricRuntimeAsync(Plan, RootPath, CancellationToken).ConfigureAwait(false);
                break;

            case LoaderType.NeoForge:
                if (string.IsNullOrWhiteSpace(Plan.LoaderVersion))
                {
                    throw new InvalidOperationException("NeoForge preparation requires a loader version.");
                }

                await SharedRuntimeDownloadSupport.DownloadVanillaRuntimeAsync(Plan, RootPath, Context, CancellationToken).ConfigureAwait(false);

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
