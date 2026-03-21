using BlockiumLauncher.Application.UseCases.Java;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Infrastructure.Java;
using BlockiumLauncher.Shared.Results;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Java;

public sealed class JavaValidationServiceTests
{
    [Fact]
    public async Task ValidateExecutableAsync_ReturnsFailure_WhenFileDoesNotExist()
    {
        var Probe = new FakeJavaVersionProbe();
        var Service = new JavaValidationService(Probe);

        var Result = await Service.ValidateExecutableAsync(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "java.exe"),
            CancellationToken.None);

        Assert.True(Result.IsFailure);
    }

    [Fact]
    public async Task ValidateExecutableAsync_ReturnsValidatedInstallation_OnSuccess()
    {
        var RootDirectory = Path.Combine(Path.GetTempPath(), "BlockiumLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootDirectory);

        var ExecutablePath = Path.Combine(RootDirectory, OperatingSystem.IsWindows() ? "java.exe" : "java");
        await File.WriteAllTextAsync(ExecutablePath, string.Empty);

        var Probe = new FakeJavaVersionProbe
        {
            ProbeResult = Result<JavaVersionParseResult>.Success(
                new JavaVersionParseResult(
                    "21.0.2",
                    JavaArchitecture.X64,
                    "Eclipse Adoptium"))
        };

        var Service = new JavaValidationService(Probe);

        var Result = await Service.ValidateExecutableAsync(ExecutablePath, CancellationToken.None);

        Assert.True(Result.IsSuccess);
        Assert.Equal("21.0.2", Result.Value.Version);
        Assert.True(Result.Value.IsValid);
    }

    private sealed class FakeJavaVersionProbe : IJavaVersionProbe
    {
        public Result<JavaVersionParseResult> ProbeResult { get; set; } =
            Result<JavaVersionParseResult>.Failure(JavaErrors.Invalid("Probe result was not configured."));

        public Task<Result<JavaVersionParseResult>> ProbeAsync(string ExecutablePath, CancellationToken CancellationToken)
        {
            return Task.FromResult(ProbeResult);
        }
    }
}
