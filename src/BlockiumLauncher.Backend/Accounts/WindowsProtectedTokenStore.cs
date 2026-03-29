using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Security;
using BlockiumLauncher.Application.UseCases.Accounts;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Security;

public sealed class WindowsProtectedTokenStore : ITokenStore
{
    private readonly string RootDirectoryPath;

    public WindowsProtectedTokenStore(string? RootDirectoryPath = null)
        : this(LauncherPaths.CreateDefault(), RootDirectoryPath)
    {
    }

    public WindowsProtectedTokenStore(ILauncherPaths launcherPaths, string? RootDirectoryPath = null)
    {
        ArgumentNullException.ThrowIfNull(launcherPaths);

        this.RootDirectoryPath = string.IsNullOrWhiteSpace(RootDirectoryPath)
            ? Path.Combine(launcherPaths.DataDirectory, "tokens")
            : Path.GetFullPath(RootDirectoryPath);
    }

    public Task<Result> SaveRefreshTokenAsync(
        AccountId AccountId,
        string RefreshToken,
        CancellationToken CancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(RefreshToken))
            {
                return Task.FromResult(Result.Failure(AccountErrors.InvalidRequest));
            }

            if (!OperatingSystem.IsWindows())
            {
                return Task.FromResult(Result.Failure(AccountErrors.PersistenceFailed));
            }

            Directory.CreateDirectory(RootDirectoryPath);

            var PlainBytes = Encoding.UTF8.GetBytes(RefreshToken);
            var ProtectedBytes = ProtectedData.Protect(PlainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);

            File.WriteAllBytes(GetTokenFilePath(AccountId), ProtectedBytes);
            return Task.FromResult(Result.Success());
        }
        catch
        {
            return Task.FromResult(Result.Failure(AccountErrors.PersistenceFailed));
        }
    }

    public Task<Result<string>> GetRefreshTokenAsync(
        AccountId AccountId,
        CancellationToken CancellationToken = default)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return Task.FromResult(Result<string>.Failure(AccountErrors.PersistenceFailed));
            }

            var TokenFilePath = GetTokenFilePath(AccountId);
            if (!File.Exists(TokenFilePath))
            {
                return Task.FromResult(Result<string>.Failure(AccountErrors.TokenMissing));
            }

            var ProtectedBytes = File.ReadAllBytes(TokenFilePath);
            var PlainBytes = ProtectedData.Unprotect(ProtectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var RefreshToken = Encoding.UTF8.GetString(PlainBytes);

            return Task.FromResult(Result<string>.Success(RefreshToken));
        }
        catch
        {
            return Task.FromResult(Result<string>.Failure(AccountErrors.PersistenceFailed));
        }
    }

    public Task<Result> DeleteRefreshTokenAsync(
        AccountId AccountId,
        CancellationToken CancellationToken = default)
    {
        try
        {
            var TokenFilePath = GetTokenFilePath(AccountId);

            if (File.Exists(TokenFilePath))
            {
                File.Delete(TokenFilePath);
            }

            return Task.FromResult(Result.Success());
        }
        catch
        {
            return Task.FromResult(Result.Failure(AccountErrors.PersistenceFailed));
        }
    }

    private string GetTokenFilePath(AccountId AccountId)
    {
        return Path.Combine(RootDirectoryPath, AccountId.ToString() + ".bin");
    }
}
