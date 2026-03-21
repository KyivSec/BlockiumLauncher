using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Java;

public sealed class DiscoverJavaUseCase
{
    private readonly IJavaInstallationRepository JavaInstallationRepository;
    private readonly IJavaDiscoveryService JavaDiscoveryService;
    private readonly IJavaValidationService JavaValidationService;

    public DiscoverJavaUseCase(
        IJavaInstallationRepository JavaInstallationRepository,
        IJavaDiscoveryService JavaDiscoveryService,
        IJavaValidationService JavaValidationService)
    {
        this.JavaInstallationRepository = JavaInstallationRepository;
        this.JavaDiscoveryService = JavaDiscoveryService;
        this.JavaValidationService = JavaValidationService;
    }

    public async Task<Result<IReadOnlyList<JavaInstallationSummary>>> ExecuteAsync(
        DiscoverJavaRequest Request,
        CancellationToken CancellationToken)
    {
        var ExistingInstallations = await JavaInstallationRepository.ListAsync(CancellationToken);
        var MergedInstallations = new Dictionary<string, JavaInstallation>(GetPathComparer());

        foreach (var ExistingInstallation in ExistingInstallations) {
            CancellationToken.ThrowIfCancellationRequested();

            var ValidationResult = await JavaValidationService.ValidateInstallationAsync(
                ExistingInstallation,
                CancellationToken);

            if (ValidationResult.IsSuccess) {
                Upsert(MergedInstallations, ValidationResult.Value);
                continue;
            }

            if (Request.IncludeInvalid) {
                Upsert(MergedInstallations, CreateInvalidCopy(ExistingInstallation));
            }
        }

        var DiscoveredInstallationsResult = await JavaDiscoveryService.DiscoverAsync(
            Request.IncludeInvalid,
            CancellationToken);

        if (DiscoveredInstallationsResult.IsFailure) {
            return Result<IReadOnlyList<JavaInstallationSummary>>.Failure(DiscoveredInstallationsResult.Error);
        }

        foreach (var DiscoveredInstallation in DiscoveredInstallationsResult.Value) {
            Upsert(MergedInstallations, DiscoveredInstallation);
        }

        foreach (var Installation in MergedInstallations.Values) {
            await JavaInstallationRepository.SaveAsync(Installation, CancellationToken);
        }

        var Summaries = MergedInstallations.Values
            .OrderBy(Item => Item.ExecutablePath, GetPathComparer())
            .Select(MapToSummary)
            .ToList();

        return Result<IReadOnlyList<JavaInstallationSummary>>.Success(Summaries);
    }

    private static void Upsert(IDictionary<string, JavaInstallation> Installations, JavaInstallation Installation)
    {
        Installations[NormalizePath(Installation.ExecutablePath)] = Installation;
    }

    private static JavaInstallation CreateInvalidCopy(JavaInstallation Installation)
    {
        return JavaInstallation.Create(
            Installation.JavaInstallationId,
            Installation.ExecutablePath,
            Installation.Version,
            Installation.Architecture,
            Installation.Vendor,
            false);
    }

    private static JavaInstallationSummary MapToSummary(JavaInstallation Installation)
    {
        return new JavaInstallationSummary(
            Installation.JavaInstallationId,
            Installation.ExecutablePath,
            Installation.Version,
            Installation.Architecture,
            Installation.Vendor,
            Installation.IsValid);
    }

    private static string NormalizePath(string PathValue)
    {
        var FullPath = Path.GetFullPath(PathValue.Trim());
        return OperatingSystem.IsWindows()
            ? FullPath.ToUpperInvariant()
            : FullPath;
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }
}
