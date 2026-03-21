using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Java;

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
