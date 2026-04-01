using System;
using BlockiumLauncher.Application.UseCases.Security;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using BlockiumLauncher.Infrastructure.Security;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Security;

public sealed class PlatformSecretStoreTests
{
    [Fact]
    public void SaveAndGetSecret_RoundTripsSecretOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var rootPath = CreateDirectory();

        try
        {
            var store = new PlatformSecretStore(new LauncherPaths(rootPath));
            var saveResult = store.SaveSecret("curseforge-api-key", "secret-value");
            var readResult = store.GetSecret("curseforge-api-key");

            Assert.True(saveResult.IsSuccess);
            Assert.True(readResult.IsSuccess);
            Assert.Equal("secret-value", readResult.Value);
        }
        finally
        {
            DeleteDirectoryIfExists(rootPath);
        }
    }

    [Fact]
    public void GetSecret_ReturnsNotFound_WhenSecretDoesNotExistOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var rootPath = CreateDirectory();

        try
        {
            var store = new PlatformSecretStore(new LauncherPaths(rootPath));
            var readResult = store.GetSecret("missing-secret");

            Assert.True(readResult.IsFailure);
            Assert.Equal(SecretStoreErrors.SecretNotFound.Code, readResult.Error.Code);
        }
        finally
        {
            DeleteDirectoryIfExists(rootPath);
        }
    }

    private static string CreateDirectory()
    {
        var pathValue = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BlockiumLauncher.Tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(pathValue);
        return pathValue;
    }

    private static void DeleteDirectoryIfExists(string pathValue)
    {
        if (System.IO.Directory.Exists(pathValue))
        {
            System.IO.Directory.Delete(pathValue, true);
        }
    }
}
