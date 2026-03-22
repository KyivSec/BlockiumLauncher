using System.Text.Json;
using BlockiumLauncher.Application.Abstractions.Diagnostics;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.Diagnostics;
using BlockiumLauncher.Application.UseCases.Accounts;
using BlockiumLauncher.Contracts.Launch;
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
        IStructuredLogger Logger,
        IOperationContextFactory OperationContextFactory)
    {
        this.InstanceRepository = InstanceRepository ?? throw new ArgumentNullException(nameof(InstanceRepository));
        this.ResolveOfflineLaunchAccountUseCase = ResolveOfflineLaunchAccountUseCase ?? throw new ArgumentNullException(nameof(ResolveOfflineLaunchAccountUseCase));
        this.VersionManifestService = VersionManifestService ?? throw new ArgumentNullException(nameof(VersionManifestService));
        this.LoaderMetadataService = LoaderMetadataService ?? throw new ArgumentNullException(nameof(LoaderMetadataService));
        this.JavaRuntimeResolver = JavaRuntimeResolver ?? throw new ArgumentNullException(nameof(JavaRuntimeResolver));
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
        var RuntimeMetadata = TryLoadRuntimeMetadata(WorkingDirectory);

        var ResolvedMainClass = ResolveMainClass(Request, RuntimeMetadata);
        if (string.IsNullOrWhiteSpace(ResolvedMainClass))
        {
            return Result<LaunchPlanDto>.Failure(LaunchErrors.MainClassMissing);
        }

        var ClasspathEntries = ResolveClasspathEntries(Request, RuntimeMetadata, WorkingDirectory);
        if (ClasspathEntries.Count == 0)
        {
            return Result<LaunchPlanDto>.Failure(LaunchErrors.ClasspathMissing);
        }

        string? AssetsDirectory = ResolveAssetsDirectory(Request, RuntimeMetadata, WorkingDirectory);
        string? AssetIndexId = ResolveAssetIndexId(Request, RuntimeMetadata);

        if (AssetsDirectory is not null && !Directory.Exists(AssetsDirectory))
        {
            return Result<LaunchPlanDto>.Failure(LaunchErrors.AssetsDirectoryMissing);
        }

        if (AssetsDirectory is not null && string.IsNullOrWhiteSpace(AssetIndexId))
        {
            return Result<LaunchPlanDto>.Failure(LaunchErrors.AssetIndexMissing);
        }

        var JvmArguments = new List<LaunchArgumentDto>
        {
            new() { Value = "-Xms" + Instance.LaunchProfile.MinMemoryMb + "m" },
            new() { Value = "-Xmx" + Instance.LaunchProfile.MaxMemoryMb + "m" }
        };

        var NativesDirectory = ResolveNativesDirectory(RuntimeMetadata, WorkingDirectory);
        if (!string.IsNullOrWhiteSpace(NativesDirectory))
        {
            JvmArguments.Add(new LaunchArgumentDto { Value = "-Djava.library.path=" + NativesDirectory });
        }

        JvmArguments.Add(new LaunchArgumentDto { Value = "-cp" });
        JvmArguments.Add(new LaunchArgumentDto { Value = string.Join(Path.PathSeparator, ClasspathEntries) });

        foreach (var Arg in Instance.LaunchProfile.ExtraJvmArgs)
        {
            JvmArguments.Add(new LaunchArgumentDto { Value = Arg });
        }

        var GameArguments = new List<LaunchArgumentDto>
        {
            new() { Value = "--username" },
            new() { Value = Account.Username },
            new() { Value = "--version" },
            new() { Value = Instance.GameVersion.ToString() },
            new() { Value = "--gameDir" },
            new() { Value = WorkingDirectory },
            new() { Value = "--uuid" },
            new() { Value = Account.PlayerUuid },
            new() { Value = "--accessToken" },
            new() { Value = "0" },
            new() { Value = "--userType" },
            new() { Value = "legacy" },
            new() { Value = "--versionType" },
            new() { Value = "release" },
            new() { Value = "--userProperties" },
            new() { Value = "{}" }
        };

        if (AssetsDirectory is not null)
        {
            GameArguments.Add(new LaunchArgumentDto { Value = "--assetsDir" });
            GameArguments.Add(new LaunchArgumentDto { Value = AssetsDirectory });
            GameArguments.Add(new LaunchArgumentDto { Value = "--assetIndex" });
            GameArguments.Add(new LaunchArgumentDto { Value = AssetIndexId! });
        }

        if (Instance.LoaderType != LoaderType.Vanilla)
        {
            var LoaderVersionText = Instance.LoaderVersion?.ToString();
            if (string.IsNullOrWhiteSpace(LoaderVersionText))
            {
                return Result<LaunchPlanDto>.Failure(LaunchErrors.LoaderMetadataMissing);
            }

            GameArguments.Add(new LaunchArgumentDto { Value = "--loader" });
            GameArguments.Add(new LaunchArgumentDto { Value = Instance.LoaderType.ToString() });
            GameArguments.Add(new LaunchArgumentDto { Value = "--loaderVersion" });
            GameArguments.Add(new LaunchArgumentDto { Value = LoaderVersionText });
        }

        foreach (var Arg in Instance.LaunchProfile.ExtraGameArgs)
        {
            GameArguments.Add(new LaunchArgumentDto { Value = Arg });
        }

        var EnvironmentVariables = Instance.LaunchProfile.EnvironmentVariables
            .Select(x => new LaunchEnvironmentVariableDto
            {
                Name = x.Key,
                Value = x.Value
            })
            .ToList();

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
            ClasspathCount = Plan.ClasspathEntries.Count,
            JvmArgumentCount = Plan.JvmArguments.Count,
            GameArgumentCount = Plan.GameArguments.Count,
            ResolvedFromRuntimeMetadata = RuntimeMetadata is not null,
            JavaExecutablePath = Plan.JavaExecutablePath
        });

        return Result<LaunchPlanDto>.Success(Plan);
    }

    private static RuntimeMetadataDto? TryLoadRuntimeMetadata(string WorkingDirectory)
    {
        var RuntimePath = Path.Combine(WorkingDirectory, ".blockium", "runtime.json");
        if (!File.Exists(RuntimePath))
        {
            return null;
        }

        var Json = File.ReadAllText(RuntimePath);
        return JsonSerializer.Deserialize<RuntimeMetadataDto>(Json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    private static string? ResolveMainClass(BuildLaunchPlanRequest Request, RuntimeMetadataDto? RuntimeMetadata)
    {
        if (!string.IsNullOrWhiteSpace(Request.MainClass))
        {
            return Request.MainClass.Trim();
        }

        if (!string.IsNullOrWhiteSpace(RuntimeMetadata?.MainClass))
        {
            return RuntimeMetadata.MainClass.Trim();
        }

        return null;
    }

    private static List<string> ResolveClasspathEntries(BuildLaunchPlanRequest Request, RuntimeMetadataDto? RuntimeMetadata, string WorkingDirectory)
    {
        var SourceEntries = Request.ClasspathEntries is not null && Request.ClasspathEntries.Count > 0
            ? Request.ClasspathEntries
            : RuntimeMetadata?.ClasspathEntries ?? [];

        var Result = new List<string>();

        foreach (var Entry in SourceEntries)
        {
            if (string.IsNullOrWhiteSpace(Entry))
            {
                continue;
            }

            var FullEntry = Path.IsPathRooted(Entry)
                ? Path.GetFullPath(Entry)
                : Path.GetFullPath(Path.Combine(WorkingDirectory, Entry));

            if (!File.Exists(FullEntry) && !Directory.Exists(FullEntry))
            {
                continue;
            }

            Result.Add(FullEntry);
        }

        return Result;
    }

    private static string? ResolveAssetsDirectory(BuildLaunchPlanRequest Request, RuntimeMetadataDto? RuntimeMetadata, string WorkingDirectory)
    {
        var Value = !string.IsNullOrWhiteSpace(Request.AssetsDirectory)
            ? Request.AssetsDirectory
            : RuntimeMetadata?.AssetsDirectory;

        if (string.IsNullOrWhiteSpace(Value))
        {
            return null;
        }

        return Path.IsPathRooted(Value)
            ? Path.GetFullPath(Value)
            : Path.GetFullPath(Path.Combine(WorkingDirectory, Value));
    }

    private static string? ResolveAssetIndexId(BuildLaunchPlanRequest Request, RuntimeMetadataDto? RuntimeMetadata)
    {
        if (!string.IsNullOrWhiteSpace(Request.AssetIndexId))
        {
            return Request.AssetIndexId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(RuntimeMetadata?.AssetIndexId))
        {
            return RuntimeMetadata.AssetIndexId.Trim();
        }

        return null;
    }

    private static string? ResolveNativesDirectory(RuntimeMetadataDto? RuntimeMetadata, string WorkingDirectory)
    {
        if (string.IsNullOrWhiteSpace(RuntimeMetadata?.NativesDirectory))
        {
            return null;
        }

        var FullPath = Path.IsPathRooted(RuntimeMetadata.NativesDirectory)
            ? Path.GetFullPath(RuntimeMetadata.NativesDirectory)
            : Path.GetFullPath(Path.Combine(WorkingDirectory, RuntimeMetadata.NativesDirectory));

        return Directory.Exists(FullPath) ? FullPath : null;
    }

    private sealed class RuntimeMetadataDto
    {
        public string Version { get; init; } = string.Empty;
        public string MainClass { get; init; } = string.Empty;
        public string ClientJarPath { get; init; } = string.Empty;
        public IReadOnlyList<string> ClasspathEntries { get; init; } = [];
        public string AssetsDirectory { get; init; } = string.Empty;
        public string AssetIndexId { get; init; } = string.Empty;
        public string NativesDirectory { get; init; } = string.Empty;
        public string[] ExtraJvmArguments { get; init; } = [];
        public string[] ExtraGameArguments { get; init; } = [];
    }
}