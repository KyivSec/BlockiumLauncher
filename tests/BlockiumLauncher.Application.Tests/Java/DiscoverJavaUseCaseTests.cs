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

public sealed class DiscoverJavaUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_MergesExistingAndDiscoveredInstallations()
    {
        var ExistingInstallation = JavaInstallation.Create(
            new JavaInstallationId("existing"),
            @"C:\Java\jdk-21\bin\java.exe",
            "21.0.2",
            JavaArchitecture.X64,
            "OpenJDK",
            true);

        var DiscoveredInstallation = JavaInstallation.Create(
            new JavaInstallationId("discovered"),
            @"C:\Java\jdk-17\bin\java.exe",
            "17.0.10",
            JavaArchitecture.X64,
            "Eclipse Adoptium",
            true);

        var Repository = new FakeJavaInstallationRepository(new[] { ExistingInstallation });
        var DiscoveryService = new FakeJavaDiscoveryService(new[] { DiscoveredInstallation });
        var ValidationService = new PassthroughJavaValidationService();

        var UseCase = new DiscoverJavaUseCase(
            Repository,
            DiscoveryService,
            ValidationService);

        var Result = await UseCase.ExecuteAsync(
            new DiscoverJavaRequest(),
            CancellationToken.None);

        Assert.True(Result.IsSuccess);
        Assert.Equal(2, Result.Value.Count);
        Assert.Equal(2, Repository.SavedInstallations.Count);
    }

    private sealed class FakeJavaInstallationRepository : IJavaInstallationRepository
    {
        private readonly List<JavaInstallation> Installations;

        public List<JavaInstallation> SavedInstallations { get; } = new();

        public FakeJavaInstallationRepository(IEnumerable<JavaInstallation> Installations)
        {
            this.Installations = Installations.ToList();
        }

        public Task<IReadOnlyList<JavaInstallation>> ListAsync(CancellationToken CancellationToken)
        {
            return Task.FromResult<IReadOnlyList<JavaInstallation>>(Installations);
        }

        public Task<JavaInstallation?> GetByIdAsync(JavaInstallationId JavaInstallationId, CancellationToken CancellationToken)
        {
            return Task.FromResult(Installations.FirstOrDefault(Item => Item.JavaInstallationId.Equals(JavaInstallationId)));
        }

        public Task SaveAsync(JavaInstallation JavaInstallation, CancellationToken CancellationToken)
        {
            SavedInstallations.Add(JavaInstallation);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(JavaInstallationId JavaInstallationId, CancellationToken CancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeJavaDiscoveryService : IJavaDiscoveryService
    {
        private readonly IReadOnlyList<JavaInstallation> Installations;

        public FakeJavaDiscoveryService(IReadOnlyList<JavaInstallation> Installations)
        {
            this.Installations = Installations;
        }

        public Task<Result<IReadOnlyList<JavaInstallation>>> DiscoverAsync(bool IncludeInvalid, CancellationToken CancellationToken)
        {
            return Task.FromResult(Result<IReadOnlyList<JavaInstallation>>.Success(Installations));
        }
    }

    private sealed class PassthroughJavaValidationService : IJavaValidationService
    {
        public Task<Result<JavaInstallation>> ValidateInstallationAsync(JavaInstallation JavaInstallation, CancellationToken CancellationToken)
        {
            return Task.FromResult(Result<JavaInstallation>.Success(JavaInstallation));
        }

        public Task<Result<JavaInstallation>> ValidateExecutableAsync(string ExecutablePath, CancellationToken CancellationToken)
        {
            return Task.FromResult(Result<JavaInstallation>.Failure(new Error("Java.Invalid", "Not used.")));
        }
    }
}
