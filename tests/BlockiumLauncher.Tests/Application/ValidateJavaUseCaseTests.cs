using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Java;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;
using Xunit;

namespace BlockiumLauncher.Application.Tests.Java;

public sealed class ValidateJavaUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenInstallationDoesNotExist()
    {
        var Repository = new FakeJavaInstallationRepository();
        var ValidationService = new FakeJavaValidationService();
        var UseCase = new ValidateJavaUseCase(Repository, ValidationService);

        var Result = await UseCase.ExecuteAsync(
            new ValidateJavaRequest(new JavaInstallationId("missing")),
            CancellationToken.None);

        Assert.True(Result.IsFailure);
    }

    [Fact]
    public async Task ExecuteAsync_SavesValidatedInstallation_OnSuccess()
    {
        var Installation = JavaInstallation.Create(
            new JavaInstallationId("known"),
            @"C:\Java\jdk-21\bin\java.exe",
            "21.0.2",
            JavaArchitecture.X64,
            "OpenJDK",
            true);

        var Repository = new FakeJavaInstallationRepository
        {
            Installation = Installation
        };

        var ValidationService = new FakeJavaValidationService
        {
            ValidationResult = Result<JavaInstallation>.Success(
                JavaInstallation.Create(
                    Installation.JavaInstallationId,
                    Installation.ExecutablePath,
                    "21.0.3",
                    JavaArchitecture.X64,
                    "Eclipse Adoptium",
                    true))
        };

        var UseCase = new ValidateJavaUseCase(Repository, ValidationService);

        var Result = await UseCase.ExecuteAsync(
            new ValidateJavaRequest(Installation.JavaInstallationId),
            CancellationToken.None);

        Assert.True(Result.IsSuccess);
        Assert.NotNull(Repository.SavedInstallation);
        Assert.Equal("21.0.3", Repository.SavedInstallation!.Version);
    }

    private sealed class FakeJavaInstallationRepository : IJavaInstallationRepository
    {
        public JavaInstallation? Installation { get; set; }
        public JavaInstallation? SavedInstallation { get; private set; }

        public Task<IReadOnlyList<JavaInstallation>> ListAsync(CancellationToken CancellationToken)
        {
            return Task.FromResult<IReadOnlyList<JavaInstallation>>(Installation is null ? Array.Empty<JavaInstallation>() : new[] { Installation });
        }

        public Task<JavaInstallation?> GetByIdAsync(JavaInstallationId JavaInstallationId, CancellationToken CancellationToken)
        {
            return Task.FromResult(Installation);
        }

        public Task SaveAsync(JavaInstallation JavaInstallation, CancellationToken CancellationToken)
        {
            SavedInstallation = JavaInstallation;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(JavaInstallationId JavaInstallationId, CancellationToken CancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeJavaValidationService : IJavaValidationService
    {
        public Result<JavaInstallation> ValidationResult { get; set; } =
            Result<JavaInstallation>.Failure(new Error("Java.Invalid", "Validation result was not configured."));

        public Task<Result<JavaInstallation>> ValidateInstallationAsync(JavaInstallation JavaInstallation, CancellationToken CancellationToken)
        {
            return Task.FromResult(ValidationResult);
        }

        public Task<Result<JavaInstallation>> ValidateExecutableAsync(string ExecutablePath, CancellationToken CancellationToken)
        {
            return Task.FromResult(ValidationResult);
        }
    }
}
