using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Instances;

public sealed class UpdateInstanceConfigurationRequest
{
    public InstanceId InstanceId { get; init; }
    public string? Name { get; init; }
    public string? IconKey { get; init; }
    public int? PreferredJavaMajor { get; init; }
    public bool SkipCompatibilityChecks { get; init; }
    public int MinimumMemoryMb { get; init; }
    public int MaximumMemoryMb { get; init; }
}

public sealed class UpdateInstanceConfigurationUseCase
{
    private readonly IInstanceRepository instanceRepository;

    public UpdateInstanceConfigurationUseCase(IInstanceRepository instanceRepository)
    {
        this.instanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
    }

    public async Task<Result<LauncherInstance>> ExecuteAsync(
        UpdateInstanceConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null ||
            request.InstanceId == default ||
            request.MinimumMemoryMb <= 0 ||
            request.MaximumMemoryMb <= 0)
        {
            return Result<LauncherInstance>.Failure(InstanceContentErrors.InvalidRequest);
        }

        var instance = await instanceRepository.GetByIdAsync(request.InstanceId, cancellationToken).ConfigureAwait(false);
        if (instance is null)
        {
            return Result<LauncherInstance>.Failure(InstanceContentErrors.InstanceNotFound);
        }

        if (!string.IsNullOrWhiteSpace(request.Name) &&
            !string.Equals(instance.Name, request.Name.Trim(), StringComparison.Ordinal))
        {
            instance.Rename(request.Name.Trim());
        }

        if (!string.Equals(instance.IconKey, NormalizeOptional(request.IconKey), StringComparison.Ordinal))
        {
            instance.ChangeIconKey(request.IconKey);
        }

        var updatedLaunchProfile = new LaunchProfile(
            request.MinimumMemoryMb,
            request.MaximumMemoryMb,
            instance.LaunchProfile.ExtraJvmArgs,
            instance.LaunchProfile.ExtraGameArgs,
            instance.LaunchProfile.EnvironmentVariables,
            request.PreferredJavaMajor,
            request.SkipCompatibilityChecks);

        instance.ChangeLaunchProfile(updatedLaunchProfile);
        await instanceRepository.SaveAsync(instance, cancellationToken).ConfigureAwait(false);
        return Result<LauncherInstance>.Success(instance);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
