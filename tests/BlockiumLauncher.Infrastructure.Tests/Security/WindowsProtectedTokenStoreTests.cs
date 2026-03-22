using System;
using System.IO;
using System.Threading.Tasks;
using BlockiumLauncher.Application.UseCases.Accounts;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Security;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Security;

public sealed class WindowsProtectedTokenStoreTests
{
    [Fact]
    public async Task SaveAndGetRefreshTokenAsync_RoundTripsToken()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var RootPath = CreateDirectory();

        try
        {
            var Store = new WindowsProtectedTokenStore(RootPath);
            var accountId = AccountId.New();

            var SaveResult = await Store.SaveRefreshTokenAsync(accountId, "refresh-token-1");
            var ReadResult = await Store.GetRefreshTokenAsync(accountId);

            Assert.True(SaveResult.IsSuccess);
            Assert.True(ReadResult.IsSuccess);
            Assert.Equal("refresh-token-1", ReadResult.Value);
        }
        finally
        {
            DeleteDirectoryIfExists(RootPath);
        }
    }

    [Fact]
    public async Task GetRefreshTokenAsync_ReturnsFailure_WhenTokenDoesNotExist()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var RootPath = CreateDirectory();

        try
        {
            var Store = new WindowsProtectedTokenStore(RootPath);
            var ReadResult = await Store.GetRefreshTokenAsync(AccountId.New());

            Assert.True(ReadResult.IsFailure);
            Assert.Equal(AccountErrors.TokenMissing.Code, ReadResult.Error.Code);
        }
        finally
        {
            DeleteDirectoryIfExists(RootPath);
        }
    }

    [Fact]
    public async Task DeleteRefreshTokenAsync_RemovesStoredToken()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var RootPath = CreateDirectory();

        try
        {
            var Store = new WindowsProtectedTokenStore(RootPath);
            var accountId = AccountId.New();

            var SaveResult = await Store.SaveRefreshTokenAsync(accountId, "refresh-token-2");
            var DeleteResult = await Store.DeleteRefreshTokenAsync(accountId);
            var ReadResult = await Store.GetRefreshTokenAsync(accountId);

            Assert.True(SaveResult.IsSuccess);
            Assert.True(DeleteResult.IsSuccess);
            Assert.True(ReadResult.IsFailure);
            Assert.Equal(AccountErrors.TokenMissing.Code, ReadResult.Error.Code);
        }
        finally
        {
            DeleteDirectoryIfExists(RootPath);
        }
    }

    private static string CreateDirectory()
    {
        var PathValue = Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(PathValue);
        return PathValue;
    }

    private static void DeleteDirectoryIfExists(string PathValue)
    {
        if (Directory.Exists(PathValue))
        {
            Directory.Delete(PathValue, true);
        }
    }
}