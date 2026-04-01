using System;
using System.IO;
using BlockiumLauncher.Application.Abstractions.Security;
using BlockiumLauncher.Backend.Catalog;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Metadata;

public sealed class CurseForgeOptionsTests
{
    [Fact]
    public void FromConfiguration_UsesEnvironmentVariable_WhenPresent()
    {
        var rootPath = CreateDirectory();
        var launcherPaths = new LauncherPaths(rootPath);
        var previousNamespaced = Environment.GetEnvironmentVariable("BLOCKIUMLAUNCHER_CURSEFORGE_API_KEY");
        var previousLegacy = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");

        try
        {
            Environment.SetEnvironmentVariable("BLOCKIUMLAUNCHER_CURSEFORGE_API_KEY", "env-key", EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", null, EnvironmentVariableTarget.Process);

            var options = CurseForgeOptions.FromConfiguration(launcherPaths, new FakeSecretStore("stored-key"));

            Assert.Equal("env-key", options.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BLOCKIUMLAUNCHER_CURSEFORGE_API_KEY", previousNamespaced, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", previousLegacy, EnvironmentVariableTarget.Process);
            DeleteDirectoryIfExists(rootPath);
        }
    }

    [Fact]
    public void FromConfiguration_UsesProtectedFile_WhenEnvironmentVariableIsMissing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var rootPath = CreateDirectory();
        var launcherPaths = new LauncherPaths(rootPath);
        var previousNamespaced = Environment.GetEnvironmentVariable("BLOCKIUMLAUNCHER_CURSEFORGE_API_KEY");
        var previousLegacy = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");

        try
        {
            Environment.SetEnvironmentVariable("BLOCKIUMLAUNCHER_CURSEFORGE_API_KEY", null, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", null, EnvironmentVariableTarget.Process);

            var options = CurseForgeOptions.FromConfiguration(launcherPaths, new FakeSecretStore("protected-key"));

            Assert.Equal("protected-key", options.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BLOCKIUMLAUNCHER_CURSEFORGE_API_KEY", previousNamespaced, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("CURSEFORGE_API_KEY", previousLegacy, EnvironmentVariableTarget.Process);
            DeleteDirectoryIfExists(rootPath);
        }
    }

    private static string CreateDirectory()
    {
        var pathValue = Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pathValue);
        return pathValue;
    }

    private static void DeleteDirectoryIfExists(string pathValue)
    {
        if (Directory.Exists(pathValue))
        {
            Directory.Delete(pathValue, true);
        }
    }

    private sealed class FakeSecretStore : ISecretStore
    {
        private readonly string? SecretValue;

        public FakeSecretStore(string? secretValue)
        {
            SecretValue = secretValue;
        }

        public string BackendName => "fake";
        public bool CanPersistSecrets => true;

        public BlockiumLauncher.Shared.Results.Result SaveSecret(string SecretName, string SecretValue) => BlockiumLauncher.Shared.Results.Result.Success();

        public BlockiumLauncher.Shared.Results.Result<string> GetSecret(string SecretName)
        {
            return string.IsNullOrWhiteSpace(SecretValue)
                ? BlockiumLauncher.Shared.Results.Result<string>.Failure(BlockiumLauncher.Application.UseCases.Security.SecretStoreErrors.SecretNotFound)
                : BlockiumLauncher.Shared.Results.Result<string>.Success(SecretValue);
        }

        public BlockiumLauncher.Shared.Results.Result DeleteSecret(string SecretName) => BlockiumLauncher.Shared.Results.Result.Success();
    }
}
