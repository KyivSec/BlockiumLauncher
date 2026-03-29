using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Java
{
    public sealed class DiscoverJavaRequest
    {
        public bool IncludeInvalid { get; }

        public DiscoverJavaRequest(bool IncludeInvalid = false)
        {
            this.IncludeInvalid = IncludeInvalid;
        }
    }

    public static class JavaErrors
    {
        public static Error NotFound(string Message, string? Details = null) => new("Java.NotFound", Message, Details);
        public static Error Invalid(string Message, string? Details = null) => new("Java.Invalid", Message, Details);
        public static Error Timeout(string Message, string? Details = null) => new("Java.Timeout", Message, Details);
        public static Error VersionProbeFailed(string Message, string? Details = null) => new("Java.VersionProbeFailed", Message, Details);
        public static Error AccessDenied(string Message, string? Details = null) => new("Java.AccessDenied", Message, Details);
    }

    public sealed class SelectJavaForInstanceRequest
    {
        public InstanceId InstanceId { get; }
        public JavaInstallationId JavaInstallationId { get; }

        public SelectJavaForInstanceRequest(InstanceId InstanceId, JavaInstallationId JavaInstallationId)
        {
            this.InstanceId = InstanceId;
            this.JavaInstallationId = JavaInstallationId;
        }
    }

    public sealed class ValidateJavaRequest
    {
        public JavaInstallationId JavaInstallationId { get; }

        public ValidateJavaRequest(JavaInstallationId JavaInstallationId)
        {
            this.JavaInstallationId = JavaInstallationId;
        }
    }
}

namespace BlockiumLauncher.Application.UseCases.Java
{
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

    public sealed class ValidateJavaUseCase
    {
        private readonly IJavaInstallationRepository JavaInstallationRepository;
        private readonly IJavaValidationService JavaValidationService;

        public ValidateJavaUseCase(
            IJavaInstallationRepository JavaInstallationRepository,
            IJavaValidationService JavaValidationService)
        {
            this.JavaInstallationRepository = JavaInstallationRepository;
            this.JavaValidationService = JavaValidationService;
        }

        public async Task<Result<JavaInstallationSummary>> ExecuteAsync(
            ValidateJavaRequest Request,
            CancellationToken CancellationToken)
        {
            var Installation = await JavaInstallationRepository.GetByIdAsync(
                Request.JavaInstallationId,
                CancellationToken);

            if (Installation is null) {
                return Result<JavaInstallationSummary>.Failure(
                    JavaErrors.NotFound(
                        "Java installation was not found.",
                        Request.JavaInstallationId.ToString()));
            }

            var ValidationResult = await JavaValidationService.ValidateInstallationAsync(
                Installation,
                CancellationToken);

            if (ValidationResult.IsFailure) {
                var InvalidInstallation = CreateInvalidCopy(Installation);
                await JavaInstallationRepository.SaveAsync(InvalidInstallation, CancellationToken);
                return Result<JavaInstallationSummary>.Failure(ValidationResult.Error);
            }

            await JavaInstallationRepository.SaveAsync(ValidationResult.Value, CancellationToken);

            return Result<JavaInstallationSummary>.Success(
                new JavaInstallationSummary(
                    ValidationResult.Value.JavaInstallationId,
                    ValidationResult.Value.ExecutablePath,
                    ValidationResult.Value.Version,
                    ValidationResult.Value.Architecture,
                    ValidationResult.Value.Vendor,
                    ValidationResult.Value.IsValid));
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
    }
}
