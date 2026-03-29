using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Java;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Java;

public sealed class JavaDiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverAsync_IncludesInvalid_WhenRequested()
    {
        var ValidationService = new FakeJavaValidationService();
        var Service = new JavaDiscoveryService(
            ValidationService,
            new JavaDiscoveryOptions
            {
                BundledRelativeDirectories = Array.Empty<string>(),
                WindowsCommonRoots = Array.Empty<string>()
            });

        var Result = await Service.DiscoverAsync(true, CancellationToken.None);

        Assert.True(Result.IsSuccess);
    }

    private sealed class FakeJavaValidationService : IJavaValidationService
    {
        public Task<Result<JavaInstallation>> ValidateInstallationAsync(JavaInstallation JavaInstallation, CancellationToken CancellationToken)
        {
            return Task.FromResult(
                Result<JavaInstallation>.Failure(
                    new Error("Java.Invalid", "Not used.")));
        }

        public Task<Result<JavaInstallation>> ValidateExecutableAsync(string ExecutablePath, CancellationToken CancellationToken)
        {
            return Task.FromResult(
                Result<JavaInstallation>.Failure(
                    new Error("Java.Invalid", "Simulated invalid executable.")));
        }
    }
}
