using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
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

    public BuildLaunchPlanUseCase(
        IInstanceRepository InstanceRepository,
        ResolveOfflineLaunchAccountUseCase ResolveOfflineLaunchAccountUseCase,
        IVersionManifestService VersionManifestService,
        ILoaderMetadataService LoaderMetadataService)
    {
        this.InstanceRepository = InstanceRepository ?? throw new ArgumentNullException(nameof(InstanceRepository));
        this.ResolveOfflineLaunchAccountUseCase = ResolveOfflineLaunchAccountUseCase ?? throw new ArgumentNullException(nameof(ResolveOfflineLaunchAccountUseCase));
        this.VersionManifestService = VersionManifestService ?? throw new ArgumentNullException(nameof(VersionManifestService));
        this.LoaderMetadataService = LoaderMetadataService ?? throw new ArgumentNullException(nameof(LoaderMetadataService));
    }

    public async Task<Result<LaunchPlanDto>> ExecuteAsync(
        BuildLaunchPlanRequest Request,
        CancellationToken CancellationToken = default)
    {
        if (Request is null)
        {
            return Result<LaunchPlanDto>.Failure(LaunchErrors.InvalidRequest);
        }

        if (string.IsNullOrWhiteSpace(Request.JavaExecutablePath))
        {
            return Result<LaunchPlanDto>.Failure(LaunchErrors.JavaExecutableMissing);
        }

        if (string.IsNullOrWhiteSpace(Request.MainClass))
        {
            return Result<LaunchPlanDto>.Failure(LaunchErrors.MainClassMissing);
        }

        var Instance = await InstanceRepository.GetByIdAsync(Request.InstanceId, CancellationToken).ConfigureAwait(false);
        if (Instance is null)
        {
            return Result<LaunchPlanDto>.Failure(LaunchErrors.InstanceNotFound);
        }

        var WorkingDirectory = Path.GetFullPath(Instance.InstallLocation);
        if (!Directory.Exists(WorkingDirectory))
        {
            return Result<LaunchPlanDto>.Failure(LaunchErrors.InstanceDirectoryMissing);
        }

        var JavaExecutablePath = Path.GetFullPath(Request.JavaExecutablePath);
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

        string? AssetsDirectory = null;
        string? AssetIndexId = null;

        if (!string.IsNullOrWhiteSpace(Request.AssetsDirectory))
        {
            AssetsDirectory = Path.GetFullPath(Request.AssetsDirectory);
            if (!Directory.Exists(AssetsDirectory))
            {
                return Result<LaunchPlanDto>.Failure(LaunchErrors.AssetsDirectoryMissing);
            }

            if (string.IsNullOrWhiteSpace(Request.AssetIndexId))
            {
                return Result<LaunchPlanDto>.Failure(LaunchErrors.AssetIndexMissing);
            }

            var TrimmedAssetIndexId = Request.AssetIndexId.Trim();
            AssetIndexId = TrimmedAssetIndexId;
        }

        if (Request.ClasspathEntries is null || Request.ClasspathEntries.Count == 0)
        {
            return Result<LaunchPlanDto>.Failure(LaunchErrors.ClasspathMissing);
        }

        var ClasspathEntries = new List<string>();
        foreach (var Entry in Request.ClasspathEntries)
        {
            if (string.IsNullOrWhiteSpace(Entry))
            {
                return Result<LaunchPlanDto>.Failure(LaunchErrors.ClasspathEntryMissing);
            }

            var FullEntry = Path.GetFullPath(Entry);
            if (!File.Exists(FullEntry) && !Directory.Exists(FullEntry))
            {
                return Result<LaunchPlanDto>.Failure(LaunchErrors.ClasspathEntryMissing);
            }

            ClasspathEntries.Add(FullEntry);
        }

        var JvmArguments = new List<LaunchArgumentDto>
        {
            new() { Value = "-Xms" + Instance.LaunchProfile.MinMemoryMb + "m" },
            new() { Value = "-Xmx" + Instance.LaunchProfile.MaxMemoryMb + "m" },
            new() { Value = "-cp" },
            new() { Value = string.Join(Path.PathSeparator, ClasspathEntries) }
        };

        foreach (var Arg in Instance.LaunchProfile.ExtraJvmArgs)
        {
            JvmArguments.Add(new LaunchArgumentDto { Value = Arg });
        }

        var GameArguments = new List<LaunchArgumentDto>
        {
            new() { Value = "--username" },
            new() { Value = Account.Username },
            new() { Value = "--uuid" },
            new() { Value = Account.PlayerUuid },
            new() { Value = "--gameDir" },
            new() { Value = WorkingDirectory },
            new() { Value = "--version" },
            new() { Value = Instance.GameVersion.ToString() }
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

        return Result<LaunchPlanDto>.Success(new LaunchPlanDto
        {
            InstanceId = Instance.InstanceId.ToString(),
            AccountId = Account.AccountId,
            JavaExecutablePath = JavaExecutablePath,
            WorkingDirectory = WorkingDirectory,
            MainClass = Request.MainClass.Trim(),
            AssetsDirectory = AssetsDirectory,
            AssetIndexId = AssetIndexId,
            ClasspathEntries = ClasspathEntries,
            JvmArguments = JvmArguments,
            GameArguments = GameArguments,
            EnvironmentVariables = EnvironmentVariables,
            IsDryRun = Request.IsDryRun
        });
    }
}