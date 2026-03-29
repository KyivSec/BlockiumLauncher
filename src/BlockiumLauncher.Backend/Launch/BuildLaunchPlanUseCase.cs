using BlockiumLauncher.Application.Abstractions.Launch;
using BlockiumLauncher.Application.Abstractions.Diagnostics;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.Diagnostics;
using BlockiumLauncher.Application.UseCases.Accounts;
using BlockiumLauncher.Contracts.Accounts;
using BlockiumLauncher.Contracts.Launch;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Launch;

public sealed class BuildLaunchPlanUseCase
{
    private readonly IInstanceRepository InstanceRepository;
    private readonly ResolveOfflineLaunchAccountUseCase ResolveOfflineLaunchAccountUseCase;
    private readonly IVersionManifestService VersionManifestService;
    private readonly ILoaderMetadataService LoaderMetadataService;
    private readonly IJavaRuntimeResolver JavaRuntimeResolver;
    private readonly IRuntimeMetadataStore RuntimeMetadataStore;
    private readonly IStructuredLogger Logger;
    private readonly IOperationContextFactory OperationContextFactory;

    public BuildLaunchPlanUseCase(
        IInstanceRepository InstanceRepository,
        ResolveOfflineLaunchAccountUseCase ResolveOfflineLaunchAccountUseCase,
        IVersionManifestService VersionManifestService,
        ILoaderMetadataService LoaderMetadataService)
        : this(
            InstanceRepository,
            ResolveOfflineLaunchAccountUseCase,
            VersionManifestService,
            LoaderMetadataService,
            NoOpJavaRuntimeResolver.Instance,
            NullRuntimeMetadataStore.Instance,
            NullStructuredLogger.Instance,
            DefaultOperationContextFactory.Instance)
    {
    }

    public BuildLaunchPlanUseCase(
        IInstanceRepository InstanceRepository,
        ResolveOfflineLaunchAccountUseCase ResolveOfflineLaunchAccountUseCase,
        IVersionManifestService VersionManifestService,
        ILoaderMetadataService LoaderMetadataService,
        IStructuredLogger Logger,
        IOperationContextFactory OperationContextFactory)
        : this(
            InstanceRepository,
            ResolveOfflineLaunchAccountUseCase,
            VersionManifestService,
            LoaderMetadataService,
            NoOpJavaRuntimeResolver.Instance,
            NullRuntimeMetadataStore.Instance,
            Logger,
            OperationContextFactory)
    {
    }

    public BuildLaunchPlanUseCase(
        IInstanceRepository InstanceRepository,
        ResolveOfflineLaunchAccountUseCase ResolveOfflineLaunchAccountUseCase,
        IVersionManifestService VersionManifestService,
        ILoaderMetadataService LoaderMetadataService,
        IJavaRuntimeResolver JavaRuntimeResolver,
        IRuntimeMetadataStore RuntimeMetadataStore,
        IStructuredLogger Logger,
        IOperationContextFactory OperationContextFactory)
    {
        this.InstanceRepository = InstanceRepository ?? throw new ArgumentNullException(nameof(InstanceRepository));
        this.ResolveOfflineLaunchAccountUseCase = ResolveOfflineLaunchAccountUseCase ?? throw new ArgumentNullException(nameof(ResolveOfflineLaunchAccountUseCase));
        this.VersionManifestService = VersionManifestService ?? throw new ArgumentNullException(nameof(VersionManifestService));
        this.LoaderMetadataService = LoaderMetadataService ?? throw new ArgumentNullException(nameof(LoaderMetadataService));
        this.JavaRuntimeResolver = JavaRuntimeResolver ?? throw new ArgumentNullException(nameof(JavaRuntimeResolver));
        this.RuntimeMetadataStore = RuntimeMetadataStore ?? throw new ArgumentNullException(nameof(RuntimeMetadataStore));
        this.Logger = Logger ?? throw new ArgumentNullException(nameof(Logger));
        this.OperationContextFactory = OperationContextFactory ?? throw new ArgumentNullException(nameof(OperationContextFactory));
    }

    public async Task<Result<LaunchPlanDto>> ExecuteAsync(
        BuildLaunchPlanRequest Request,
        CancellationToken CancellationToken = default)
    {
        var Context = OperationContextFactory.Create("BuildLaunchPlan");

        if (Request is null)
        {
            Logger.Warning(Context, nameof(BuildLaunchPlanUseCase), "InvalidRequest", "Launch plan request was null.");
            return Result<LaunchPlanDto>.Failure(LaunchErrors.InvalidRequest);
        }

        Logger.Info(Context, nameof(BuildLaunchPlanUseCase), "LaunchPlanStarted", "Launch plan build started.", new
        {
            InstanceId = Request.InstanceId.ToString(),
            AccountId = Request.AccountId?.ToString(),
            Request.JavaExecutablePath,
            Request.MainClass,
            Request.IsDryRun
        });

        var Instance = await InstanceRepository.GetByIdAsync(Request.InstanceId, CancellationToken).ConfigureAwait(false);
        if (Instance is null)
        {
            Logger.Warning(Context, nameof(BuildLaunchPlanUseCase), "InstanceNotFound", "Launch plan instance was not found.", new
            {
                InstanceId = Request.InstanceId.ToString()
            });
            return Result<LaunchPlanDto>.Failure(LaunchErrors.InstanceNotFound);
        }

        var WorkingDirectory = Path.GetFullPath(Instance.InstallLocation);
        if (!Directory.Exists(WorkingDirectory))
        {
            return Result<LaunchPlanDto>.Failure(LaunchErrors.InstanceDirectoryMissing);
        }

        string JavaExecutablePath;
        if (string.IsNullOrWhiteSpace(Request.JavaExecutablePath))
        {
            var JavaResolveResult = await JavaRuntimeResolver.ResolveExecutablePathAsync(
                Instance.GameVersion.ToString(),
                CancellationToken).ConfigureAwait(false);

            if (JavaResolveResult.IsFailure)
            {
                Logger.Warning(Context, nameof(BuildLaunchPlanUseCase), "JavaAutoResolveFailed", "Automatic Java resolution failed.", new
                {
                    JavaResolveResult.Error.Code,
                    JavaResolveResult.Error.Message
                });
                return Result<LaunchPlanDto>.Failure(LaunchErrors.JavaExecutableMissing);
            }

            JavaExecutablePath = Path.GetFullPath(JavaResolveResult.Value);
        }
        else
        {
            JavaExecutablePath = Path.GetFullPath(Request.JavaExecutablePath);
        }

        if (!File.Exists(JavaExecutablePath))
        {
            return Result<LaunchPlanDto>.Failure(LaunchErrors.JavaExecutableMissing);
        }

        var VersionResult = await VersionManifestService.GetVersionAsync(Instance.GameVersion, CancellationToken).ConfigureAwait(false);
        if (VersionResult.IsFailure || VersionResult.Value is null)
        {
            return Result<LaunchPlanDto>.Failure(LaunchErrors.VersionMetadataMissing);
        }

        if (Instance.LoaderType != LoaderType.Vanilla)
        {
            var LoaderResult = await LoaderMetadataService.GetLoaderVersionsAsync(
                Instance.LoaderType,
                Instance.GameVersion,
                CancellationToken).ConfigureAwait(false);

            if (LoaderResult.IsFailure)
            {
                return Result<LaunchPlanDto>.Failure(LaunchErrors.LoaderMetadataMissing);
            }

            var HasLoaderVersion = LoaderResult.Value.Any(x =>
                string.Equals(x.LoaderVersion, Instance.LoaderVersion?.ToString(), StringComparison.OrdinalIgnoreCase));

            if (!HasLoaderVersion)
            {
                return Result<LaunchPlanDto>.Failure(LaunchErrors.LoaderMetadataMissing);
            }
        }

        var AccountResult = await ResolveOfflineLaunchAccountUseCase.ExecuteAsync(
            new ResolveOfflineLaunchAccountRequest
            {
                AccountId = Request.AccountId
            },
            CancellationToken).ConfigureAwait(false);

        if (AccountResult.IsFailure)
        {
            return Result<LaunchPlanDto>.Failure(AccountResult.Error);
        }

        var Account = AccountResult.Value;
        var RuntimeMetadata = await RuntimeMetadataStore.LoadAsync(WorkingDirectory, CancellationToken).ConfigureAwait(false);

        var ResolvedMainClass = LaunchPlanRuntimeSupport.ResolveMainClass(Request, RuntimeMetadata);
        if (string.IsNullOrWhiteSpace(ResolvedMainClass))
        {
            return Result<LaunchPlanDto>.Failure(LaunchErrors.MainClassMissing);
        }

        var ClasspathEntries = LaunchPlanRuntimeSupport.ResolveClasspathEntries(Request, RuntimeMetadata, WorkingDirectory);
        if (ClasspathEntries.Count == 0)
        {
            return Result<LaunchPlanDto>.Failure(LaunchErrors.ClasspathMissing);
        }

        var AssetsDirectory = LaunchPlanRuntimeSupport.ResolveAssetsDirectory(Request, RuntimeMetadata, WorkingDirectory);
        var AssetIndexId = LaunchPlanRuntimeSupport.ResolveAssetIndexId(Request, RuntimeMetadata);

        if (AssetsDirectory is not null && !Directory.Exists(AssetsDirectory))
        {
            return Result<LaunchPlanDto>.Failure(LaunchErrors.AssetsDirectoryMissing);
        }

        if (AssetsDirectory is not null && string.IsNullOrWhiteSpace(AssetIndexId))
        {
            return Result<LaunchPlanDto>.Failure(LaunchErrors.AssetIndexMissing);
        }

        var NativesDirectory = LaunchPlanRuntimeSupport.ResolveNativesDirectory(RuntimeMetadata, WorkingDirectory);
        var ClasspathText = string.Join(Path.PathSeparator, ClasspathEntries);

        var TokenMap = LaunchPlanTokenSupport.BuildTokenMap(
            Instance,
            Account,
            WorkingDirectory,
            AssetsDirectory,
            AssetIndexId,
            NativesDirectory,
            ResolvedMainClass,
            ClasspathEntries,
            ClasspathText,
            RuntimeMetadata);

        List<LaunchArgumentDto> JvmArguments = LaunchPlanArgumentBuilder.BuildJvmArguments(
            Instance,
            NativesDirectory,
            ClasspathText,
            TokenMap,
            RuntimeMetadata?.ExtraJvmArguments);

        List<LaunchArgumentDto> GameArguments;
        try
        {
            GameArguments = LaunchPlanArgumentBuilder.BuildGameArguments(
                Instance,
                WorkingDirectory,
                AssetsDirectory,
                AssetIndexId,
                Account,
                TokenMap,
                RuntimeMetadata?.ExtraGameArguments);
        }
        catch (InvalidOperationException)
        {
            return Result<LaunchPlanDto>.Failure(LaunchErrors.LoaderMetadataMissing);
        }

        var EnvironmentVariables = LaunchPlanArgumentBuilder.BuildEnvironmentVariables(Instance, TokenMap);

        var Plan = new LaunchPlanDto
        {
            InstanceId = Instance.InstanceId.ToString(),
            AccountId = Account.AccountId,
            JavaExecutablePath = JavaExecutablePath,
            WorkingDirectory = WorkingDirectory,
            MainClass = ResolvedMainClass,
            AssetsDirectory = AssetsDirectory,
            AssetIndexId = AssetIndexId,
            ClasspathEntries = ClasspathEntries,
            JvmArguments = JvmArguments,
            GameArguments = GameArguments,
            EnvironmentVariables = EnvironmentVariables,
            IsDryRun = Request.IsDryRun
        };

        Logger.Info(Context, nameof(BuildLaunchPlanUseCase), "LaunchPlanCompleted", "Launch plan build completed.", new
        {
            Plan.InstanceId,
            Plan.AccountId,
            Plan.MainClass,
            ClasspathCount = Plan.ClasspathEntries.Count,
            JvmArgumentCount = Plan.JvmArguments.Count,
            GameArgumentCount = Plan.GameArguments.Count,
            ResolvedFromRuntimeMetadata = RuntimeMetadata is not null,
            JavaExecutablePath = Plan.JavaExecutablePath
        });

        return Result<LaunchPlanDto>.Success(Plan);
    }

}
