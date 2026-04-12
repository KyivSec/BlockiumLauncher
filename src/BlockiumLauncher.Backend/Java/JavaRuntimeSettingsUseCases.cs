using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Java;

public sealed class ManagedJavaRuntimeSlotSummary
{
    public int JavaMajor { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public bool IsInstalled { get; init; }
    public bool IsValid { get; init; }
    public bool IsDefault { get; init; }
    public string? ExecutablePath { get; init; }
    public string? Version { get; init; }
    public string? Vendor { get; init; }
}

public sealed class GetLauncherRuntimeSettingsUseCase
{
    private readonly ILauncherRuntimeSettingsRepository launcherRuntimeSettingsRepository;

    public GetLauncherRuntimeSettingsUseCase(ILauncherRuntimeSettingsRepository launcherRuntimeSettingsRepository)
    {
        this.launcherRuntimeSettingsRepository = launcherRuntimeSettingsRepository ?? throw new ArgumentNullException(nameof(launcherRuntimeSettingsRepository));
    }

    public Task<LauncherRuntimeSettings> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        return launcherRuntimeSettingsRepository.LoadAsync(cancellationToken);
    }
}

public sealed class SaveLauncherRuntimeSettingsUseCase
{
    private readonly ILauncherRuntimeSettingsRepository launcherRuntimeSettingsRepository;

    public SaveLauncherRuntimeSettingsUseCase(ILauncherRuntimeSettingsRepository launcherRuntimeSettingsRepository)
    {
        this.launcherRuntimeSettingsRepository = launcherRuntimeSettingsRepository ?? throw new ArgumentNullException(nameof(launcherRuntimeSettingsRepository));
    }

    public Task ExecuteAsync(LauncherRuntimeSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return launcherRuntimeSettingsRepository.SaveAsync(settings.Normalize(), cancellationToken);
    }
}

public sealed class GetManagedJavaRuntimeSlotsUseCase
{
    private static readonly int[] SupportedJavaMajors = [25, 21, 17, 8];

    private readonly IManagedJavaRuntimeService managedJavaRuntimeService;
    private readonly ILauncherRuntimeSettingsRepository launcherRuntimeSettingsRepository;

    public GetManagedJavaRuntimeSlotsUseCase(
        IManagedJavaRuntimeService managedJavaRuntimeService,
        ILauncherRuntimeSettingsRepository launcherRuntimeSettingsRepository)
    {
        this.managedJavaRuntimeService = managedJavaRuntimeService ?? throw new ArgumentNullException(nameof(managedJavaRuntimeService));
        this.launcherRuntimeSettingsRepository = launcherRuntimeSettingsRepository ?? throw new ArgumentNullException(nameof(launcherRuntimeSettingsRepository));
    }

    public async Task<Result<IReadOnlyList<ManagedJavaRuntimeSlotSummary>>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var settings = await launcherRuntimeSettingsRepository.LoadAsync(cancellationToken).ConfigureAwait(false);
        var tasks = SupportedJavaMajors
            .Select(async javaMajor => new
            {
                JavaMajor = javaMajor,
                Result = await managedJavaRuntimeService.GetInstalledRuntimeAsync(javaMajor, cancellationToken).ConfigureAwait(false)
            })
            .ToArray();

        var installations = await Task.WhenAll(tasks).ConfigureAwait(false);
        var failure = installations.FirstOrDefault(item => item.Result.IsFailure);
        if (failure is not null && failure.Result.IsFailure)
        {
            return Result<IReadOnlyList<ManagedJavaRuntimeSlotSummary>>.Failure(failure.Result.Error);
        }

        var slots = installations
            .Select(item => MapToSummary(item.JavaMajor, item.Result.Value, settings.DefaultJavaMajor))
            .OrderByDescending(static slot => slot.JavaMajor)
            .ToArray();

        return Result<IReadOnlyList<ManagedJavaRuntimeSlotSummary>>.Success(slots);
    }

    private static ManagedJavaRuntimeSlotSummary MapToSummary(int javaMajor, JavaInstallation? installation, int defaultJavaMajor)
    {
        return new ManagedJavaRuntimeSlotSummary
        {
            JavaMajor = javaMajor,
            DisplayName = $"Java {javaMajor}",
            IsInstalled = installation is not null,
            IsValid = installation?.IsValid ?? false,
            IsDefault = NormalizeManagedJavaMajor(defaultJavaMajor) == javaMajor,
            ExecutablePath = installation?.ExecutablePath,
            Version = installation?.Version,
            Vendor = installation?.Vendor
        };
    }

    private static int NormalizeManagedJavaMajor(int javaMajor)
    {
        return javaMajor switch
        {
            25 => 25,
            21 => 21,
            17 => 17,
            16 => 17,
            _ when javaMajor <= 8 => 8,
            _ when javaMajor < 17 => 17,
            _ when javaMajor < 21 => 17,
            _ when javaMajor < 25 => 21,
            _ => 25
        };
    }
}

public sealed class InstallManagedJavaRuntimeRequest
{
    public int JavaMajor { get; init; }
    public bool ForceReinstall { get; init; }
}

public sealed class InstallManagedJavaRuntimeUseCase
{
    private readonly IManagedJavaRuntimeService managedJavaRuntimeService;

    public InstallManagedJavaRuntimeUseCase(IManagedJavaRuntimeService managedJavaRuntimeService)
    {
        this.managedJavaRuntimeService = managedJavaRuntimeService ?? throw new ArgumentNullException(nameof(managedJavaRuntimeService));
    }

    public Task<Result<JavaInstallation>> ExecuteAsync(
        InstallManagedJavaRuntimeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return managedJavaRuntimeService.InstallRuntimeAsync(request.JavaMajor, request.ForceReinstall, cancellationToken);
    }
}
