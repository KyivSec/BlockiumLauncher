using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Java;

public sealed class ManagedJavaRuntimeResolver : IJavaRuntimeResolver
{
    private readonly IManagedJavaRuntimeService managedJavaRuntimeService;
    private readonly ILauncherRuntimeSettingsRepository launcherRuntimeSettingsRepository;
    private readonly IJavaRequirementResolver javaRequirementResolver;

    public ManagedJavaRuntimeResolver(
        IManagedJavaRuntimeService managedJavaRuntimeService,
        ILauncherRuntimeSettingsRepository launcherRuntimeSettingsRepository,
        IJavaRequirementResolver javaRequirementResolver)
    {
        this.managedJavaRuntimeService = managedJavaRuntimeService ?? throw new ArgumentNullException(nameof(managedJavaRuntimeService));
        this.launcherRuntimeSettingsRepository = launcherRuntimeSettingsRepository ?? throw new ArgumentNullException(nameof(launcherRuntimeSettingsRepository));
        this.javaRequirementResolver = javaRequirementResolver ?? throw new ArgumentNullException(nameof(javaRequirementResolver));
    }

    public async Task<Result<string>> ResolveExecutablePathAsync(
        string minecraftVersion,
        LoaderType loaderType,
        int? preferredJavaMajor = null,
        bool skipCompatibilityChecks = false,
        CancellationToken cancellationToken = default)
    {
        var settings = await launcherRuntimeSettingsRepository.LoadAsync(cancellationToken).ConfigureAwait(false);
        var normalizedSettings = settings.Normalize();
        var requiredJavaMajor = ManagedJavaRuntimeService.NormalizeManagedJavaMajor(
            javaRequirementResolver.GetRequiredJavaMajor(minecraftVersion, loaderType));
        var effectivePreferredJavaMajor = ManagedJavaRuntimeService.NormalizeManagedJavaMajor(
            preferredJavaMajor ?? normalizedSettings.DefaultJavaMajor);
        var effectiveSkipCompatibilityChecks = skipCompatibilityChecks || normalizedSettings.SkipCompatibilityChecks;

        var effectiveJavaMajor = effectiveSkipCompatibilityChecks ||
                                 javaRequirementResolver.IsSatisfiedBy(effectivePreferredJavaMajor, minecraftVersion, loaderType)
            ? effectivePreferredJavaMajor
            : requiredJavaMajor;

        var installResult = await managedJavaRuntimeService
            .InstallRuntimeAsync(effectiveJavaMajor, forceReinstall: false, cancellationToken)
            .ConfigureAwait(false);

        if (installResult.IsFailure)
        {
            return Result<string>.Failure(installResult.Error);
        }

        return Result<string>.Success(installResult.Value.ExecutablePath);
    }
}
