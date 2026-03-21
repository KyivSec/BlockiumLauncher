using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Java;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Java;

public sealed class JavaValidationService : IJavaValidationService
{
    private readonly IJavaVersionProbe JavaVersionProbe;

    public JavaValidationService(IJavaVersionProbe JavaVersionProbe)
    {
        this.JavaVersionProbe = JavaVersionProbe;
    }

    public async Task<Result<JavaInstallation>> ValidateInstallationAsync(
        JavaInstallation JavaInstallation,
        CancellationToken CancellationToken)
    {
        var ValidationResult = await ValidateExecutableAsync(JavaInstallation.ExecutablePath, CancellationToken);

        if (ValidationResult.IsFailure) {
            return Result<JavaInstallation>.Failure(ValidationResult.Error);
        }

        return Result<JavaInstallation>.Success(
            JavaInstallation.Create(
                JavaInstallation.JavaInstallationId,
                ValidationResult.Value.ExecutablePath,
                ValidationResult.Value.Version,
                ValidationResult.Value.Architecture,
                ValidationResult.Value.Vendor,
                true));
    }

    public async Task<Result<JavaInstallation>> ValidateExecutableAsync(
        string ExecutablePath,
        CancellationToken CancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ExecutablePath)) {
            return Result<JavaInstallation>.Failure(
                JavaErrors.NotFound("Java executable path is empty."));
        }

        var FullPath = Path.GetFullPath(ExecutablePath.Trim());

        if (!File.Exists(FullPath)) {
            return Result<JavaInstallation>.Failure(
                JavaErrors.NotFound(
                    "Java executable was not found.",
                    FullPath));
        }

        var ProbeResult = await JavaVersionProbe.ProbeAsync(FullPath, CancellationToken);

        if (ProbeResult.IsFailure) {
            return Result<JavaInstallation>.Failure(ProbeResult.Error);
        }

        return Result<JavaInstallation>.Success(
            JavaInstallation.Create(
                JavaInstallationId.New(),
                FullPath,
                ProbeResult.Value.Version,
                ProbeResult.Value.Architecture,
                ProbeResult.Value.Vendor,
                true));
    }
}
