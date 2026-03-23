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

        var AssetsDirectory = ResolveAssetsDirectory(Request, RuntimeMetadata, WorkingDirectory);
        var AssetIndexId = ResolveAssetIndexId(Request, RuntimeMetadata);

        if (AssetsDirectory is not null && !Directory.Exists(AssetsDirectory))
        {
            return Result<LaunchPlanDto>.Failure(LaunchErrors.AssetsDirectoryMissing);
        }

        if (AssetsDirectory is not null && string.IsNullOrWhiteSpace(AssetIndexId))
        {
            return Result<LaunchPlanDto>.Failure(LaunchErrors.AssetIndexMissing);
        }

        var NativesDirectory = ResolveNativesDirectory(RuntimeMetadata, WorkingDirectory);
        var ClasspathText = string.Join(Path.PathSeparator, ClasspathEntries);

        var TokenMap = BuildTokenMap(
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

        var JvmArguments = new List<LaunchArgumentDto>
        {
            new() { Value = "-Xms" + Instance.LaunchProfile.MinMemoryMb + "m" },
            new() { Value = "-Xmx" + Instance.LaunchProfile.MaxMemoryMb + "m" }
        };

        if (!string.IsNullOrWhiteSpace(NativesDirectory))
        {
            JvmArguments.Add(new LaunchArgumentDto
            {
                Value = "-Djava.library.path=" + NativesDirectory
            });
        }

        foreach (var Arg in ResolveRuntimeArguments(RuntimeMetadata?.ExtraJvmArguments, TokenMap))
        {
            JvmArguments.Add(new LaunchArgumentDto { Value = Arg });
        }

        JvmArguments.Add(new LaunchArgumentDto { Value = "-cp" });
        JvmArguments.Add(new LaunchArgumentDto { Value = ClasspathText });

        foreach (var Arg in Instance.LaunchProfile.ExtraJvmArgs)
        {
            if (!string.IsNullOrWhiteSpace(Arg))
            {
                JvmArguments.Add(new LaunchArgumentDto
                {
                    Value = ExpandTokens(Arg, TokenMap)
                });
            }
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

        foreach (var Arg in ResolveRuntimeArguments(RuntimeMetadata?.ExtraGameArguments, TokenMap))
        {
            GameArguments.Add(new LaunchArgumentDto { Value = Arg });
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
            if (!string.IsNullOrWhiteSpace(Arg))
            {
                GameArguments.Add(new LaunchArgumentDto
                {
                    Value = ExpandTokens(Arg, TokenMap)
                });
            }
        }

        var EnvironmentVariables = Instance.LaunchProfile.EnvironmentVariables
            .Select(x => new LaunchEnvironmentVariableDto
            {
                Name = x.Key,
                Value = ExpandTokens(x.Value, TokenMap)
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
            Plan.MainClass,
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

    private static List<string> ResolveClasspathEntries(
        BuildLaunchPlanRequest Request,
        RuntimeMetadataDto? RuntimeMetadata,
        string WorkingDirectory)
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

            if (!Result.Contains(FullEntry, StringComparer.OrdinalIgnoreCase))
            {
                Result.Add(FullEntry);
            }
        }

        return Result;
    }

    private static string? ResolveAssetsDirectory(
        BuildLaunchPlanRequest Request,
        RuntimeMetadataDto? RuntimeMetadata,
        string WorkingDirectory)
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

    private static Dictionary<string, string> BuildTokenMap(
        dynamic Instance,
        dynamic Account,
        string WorkingDirectory,
        string? AssetsDirectory,
        string? AssetIndexId,
        string? NativesDirectory,
        string ResolvedMainClass,
        IReadOnlyList<string> ClasspathEntries,
        string ClasspathText,
        RuntimeMetadataDto? RuntimeMetadata)
    {
        var GameVersion = Instance.GameVersion?.ToString() ?? string.Empty;
        var LoaderVersion = Instance.LoaderVersion?.ToString() ?? string.Empty;
        var VersionName = !string.IsNullOrWhiteSpace(RuntimeMetadata?.Version)
            ? RuntimeMetadata.Version
            : (!string.IsNullOrWhiteSpace(LoaderVersion) ? LoaderVersion : GameVersion);

        var LibraryDirectory = TryResolveLibraryDirectory(ClasspathEntries, RuntimeMetadata);

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["${auth_player_name}"] = Account.Username ?? string.Empty,
            ["${version_name}"] = VersionName,
            ["${game_directory}"] = WorkingDirectory,
            ["${game_assets}"] = AssetsDirectory ?? string.Empty,
            ["${assets_root}"] = AssetsDirectory ?? string.Empty,
            ["${assets_index_name}"] = AssetIndexId ?? string.Empty,
            ["${auth_uuid}"] = Account.PlayerUuid ?? string.Empty,
            ["${auth_access_token}"] = "0",
            ["${auth_session}"] = "0",
            ["${user_type}"] = "legacy",
            ["${version_type}"] = "release",
            ["${natives_directory}"] = NativesDirectory ?? string.Empty,
            ["${launcher_name}"] = "BlockiumLauncher",
            ["${launcher_version}"] = "1.0.0",
            ["${classpath}"] = ClasspathText,
            ["${classpath_separator}"] = Path.PathSeparator.ToString(),
            ["${library_directory}"] = LibraryDirectory,
            ["${user_properties}"] = "{}",
            ["${clientid}"] = string.Empty,
            ["${auth_xuid}"] = string.Empty,
            ["${resolution_width}"] = string.Empty,
            ["${resolution_height}"] = string.Empty,
            ["${main_class}"] = ResolvedMainClass
        };
    }

    private static string TryResolveLibraryDirectory(
        IReadOnlyList<string> ClasspathEntries,
        RuntimeMetadataDto? RuntimeMetadata)
    {
        if (!string.IsNullOrWhiteSpace(RuntimeMetadata?.LibraryDirectory))
        {
            return RuntimeMetadata.LibraryDirectory;
        }

        foreach (var Entry in ClasspathEntries)
        {
            var DirectoryPath = File.Exists(Entry) ? Path.GetDirectoryName(Entry) : Entry;
            if (string.IsNullOrWhiteSpace(DirectoryPath))
            {
                continue;
            }

            var Current = new DirectoryInfo(DirectoryPath);
            while (Current is not null)
            {
                if (string.Equals(Current.Name, "libraries", StringComparison.OrdinalIgnoreCase))
                {
                    return Current.FullName;
                }

                Current = Current.Parent;
            }
        }

        return string.Empty;
    }

    
    private static string NormalizeJvmArgument(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return System.Text.RegularExpressions.Regex.Replace(
            value,
            @"^(-D[^=\s]+)=\s+(.+?)\s*$",
            "$1=$2");
    }
    private static IEnumerable<string> ResolveRuntimeArguments(
        IEnumerable<string>? SourceArguments,
        IReadOnlyDictionary<string, string> TokenMap)
    {
        if (SourceArguments is null)
        {
            yield break;
        }

        foreach (var Arg in SourceArguments)
        {
            if (string.IsNullOrWhiteSpace(Arg))
            {
                continue;
            }

            var Expanded = NormalizeJvmArgument(ExpandTokens(Arg, TokenMap)).Trim();
            if (Expanded.Length == 0)
            {
                continue;
            }

            foreach (var Token in SplitCommandLineArguments(Expanded))
            {
                if (!string.IsNullOrWhiteSpace(Token))
                {
                    yield return Token;
                }
            }
        }
    }

    private static IEnumerable<string> SplitCommandLineArguments(string Value)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            yield break;
        }

        var Buffer = new System.Text.StringBuilder();
        var InQuotes = false;

        for (var Index = 0; Index < Value.Length; Index++)
        {
            var Character = Value[Index];

            if (Character == '"')
            {
                InQuotes = !InQuotes;
                continue;
            }

            if (char.IsWhiteSpace(Character) && !InQuotes)
            {
                if (Buffer.Length > 0)
                {
                    yield return Buffer.ToString();
                    Buffer.Clear();
                }

                continue;
            }

            Buffer.Append(Character);
        }

        if (Buffer.Length > 0)
        {
            yield return Buffer.ToString();
        }
    }
    private static string ExpandTokens(string Value, IReadOnlyDictionary<string, string> TokenMap)
    {
        var Result = Value;

        foreach (var Pair in TokenMap)
        {
            Result = Result.Replace(Pair.Key, Pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return Result;
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
        public string LibraryDirectory { get; init; } = string.Empty;
        public string[] ExtraJvmArguments { get; init; } = [];
        public string[] ExtraGameArguments { get; init; } = [];
    }
}