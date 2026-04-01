using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using BlockiumLauncher.Application.Abstractions.Security;
using BlockiumLauncher.Application.UseCases.Security;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Security;

public sealed class PlatformSecretStore : ISecretStore
{
    private readonly ISecretStore Backend;

    public string BackendName => Backend.BackendName;
    public bool CanPersistSecrets => Backend.CanPersistSecrets;

    public PlatformSecretStore(ILauncherPaths launcherPaths)
        : this(launcherPaths, new ProcessCommandRunner())
    {
    }

    internal PlatformSecretStore(ILauncherPaths launcherPaths, IProcessCommandRunner processCommandRunner)
    {
        ArgumentNullException.ThrowIfNull(launcherPaths);
        ArgumentNullException.ThrowIfNull(processCommandRunner);

        if (OperatingSystem.IsWindows())
        {
            Backend = new WindowsFileSecretStore(launcherPaths);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            Backend = new MacOsKeychainSecretStore(processCommandRunner);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            Backend = new LinuxSecretServiceSecretStore(processCommandRunner);
            return;
        }

        Backend = new UnsupportedSecretStore();
    }

    public Result SaveSecret(string SecretName, string SecretValue) => Backend.SaveSecret(SecretName, SecretValue);
    public Result<string> GetSecret(string SecretName) => Backend.GetSecret(SecretName);
    public Result DeleteSecret(string SecretName) => Backend.DeleteSecret(SecretName);

    private sealed class WindowsFileSecretStore : ISecretStore
    {
        private readonly ILauncherPaths LauncherPaths;

        public WindowsFileSecretStore(ILauncherPaths launcherPaths)
        {
            LauncherPaths = launcherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
        }

        public string BackendName => "windows-dpapi";
        public bool CanPersistSecrets => true;

        [SupportedOSPlatform("windows")]
        public Result SaveSecret(string SecretName, string SecretValue)
        {
            if (string.IsNullOrWhiteSpace(SecretName) || string.IsNullOrWhiteSpace(SecretValue))
            {
                return Result.Failure(SecretStoreErrors.InvalidRequest);
            }

            try
            {
                var filePath = GetSecretFilePath(SecretName);
                var directoryPath = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                var plainBytes = Encoding.UTF8.GetBytes(SecretValue);
                var protectedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(filePath, protectedBytes);
                return Result.Success();
            }
            catch
            {
                return Result.Failure(SecretStoreErrors.PersistenceFailed);
            }
        }

        [SupportedOSPlatform("windows")]
        public Result<string> GetSecret(string SecretName)
        {
            if (string.IsNullOrWhiteSpace(SecretName))
            {
                return Result<string>.Failure(SecretStoreErrors.InvalidRequest);
            }

            try
            {
                var filePath = GetSecretFilePath(SecretName);
                if (!File.Exists(filePath))
                {
                    return Result<string>.Failure(SecretStoreErrors.SecretNotFound);
                }

                var protectedBytes = File.ReadAllBytes(filePath);
                var plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
                var secretValue = Encoding.UTF8.GetString(plainBytes);

                return string.IsNullOrWhiteSpace(secretValue)
                    ? Result<string>.Failure(SecretStoreErrors.SecretNotFound)
                    : Result<string>.Success(secretValue);
            }
            catch
            {
                return Result<string>.Failure(SecretStoreErrors.PersistenceFailed);
            }
        }

        [SupportedOSPlatform("windows")]
        public Result DeleteSecret(string SecretName)
        {
            if (string.IsNullOrWhiteSpace(SecretName))
            {
                return Result.Failure(SecretStoreErrors.InvalidRequest);
            }

            try
            {
                var filePath = GetSecretFilePath(SecretName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                return Result.Success();
            }
            catch
            {
                return Result.Failure(SecretStoreErrors.PersistenceFailed);
            }
        }

        private string GetSecretFilePath(string secretName)
        {
            return Path.Combine(
                LauncherPaths.DataDirectory,
                "secrets",
                SanitizeFileName(secretName) + ".bin");
        }

        private static string SanitizeFileName(string value)
        {
            var buffer = value.ToCharArray();
            var invalidChars = Path.GetInvalidFileNameChars();

            for (var index = 0; index < buffer.Length; index++)
            {
                if (invalidChars.Contains(buffer[index]))
                {
                    buffer[index] = '-';
                }
            }

            return new string(buffer);
        }
    }

    private sealed class MacOsKeychainSecretStore : ISecretStore
    {
        private const string ServiceName = "BlockiumLauncher";

        private readonly IProcessCommandRunner ProcessCommandRunner;

        public MacOsKeychainSecretStore(IProcessCommandRunner processCommandRunner)
        {
            ProcessCommandRunner = processCommandRunner ?? throw new ArgumentNullException(nameof(processCommandRunner));
        }

        public string BackendName => "macos-keychain";
        public bool CanPersistSecrets => ProcessCommandRunner.CanStart("security");

        public Result SaveSecret(string SecretName, string SecretValue)
        {
            if (string.IsNullOrWhiteSpace(SecretName) || string.IsNullOrWhiteSpace(SecretValue))
            {
                return Result.Failure(SecretStoreErrors.InvalidRequest);
            }

            var commandResult = ProcessCommandRunner.Run(
                "security",
                ["add-generic-password", "-U", "-a", SecretName, "-s", ServiceName, "-w", SecretValue]);

            if (commandResult.IsCommandMissing)
            {
                return Result.Failure(SecretStoreErrors.StoreUnavailable);
            }

            return commandResult.ExitCode == 0
                ? Result.Success()
                : Result.Failure(new BlockiumLauncher.Shared.Errors.Error(
                    SecretStoreErrors.PersistenceFailed.Code,
                    SecretStoreErrors.PersistenceFailed.Message,
                    commandResult.StandardError));
        }

        public Result<string> GetSecret(string SecretName)
        {
            if (string.IsNullOrWhiteSpace(SecretName))
            {
                return Result<string>.Failure(SecretStoreErrors.InvalidRequest);
            }

            var commandResult = ProcessCommandRunner.Run(
                "security",
                ["find-generic-password", "-w", "-a", SecretName, "-s", ServiceName]);

            if (commandResult.IsCommandMissing)
            {
                return Result<string>.Failure(SecretStoreErrors.StoreUnavailable);
            }

            if (commandResult.ExitCode != 0)
            {
                return Result<string>.Failure(SecretStoreErrors.SecretNotFound);
            }

            var secretValue = commandResult.StandardOutput.Trim();
            return string.IsNullOrWhiteSpace(secretValue)
                ? Result<string>.Failure(SecretStoreErrors.SecretNotFound)
                : Result<string>.Success(secretValue);
        }

        public Result DeleteSecret(string SecretName)
        {
            if (string.IsNullOrWhiteSpace(SecretName))
            {
                return Result.Failure(SecretStoreErrors.InvalidRequest);
            }

            var commandResult = ProcessCommandRunner.Run(
                "security",
                ["delete-generic-password", "-a", SecretName, "-s", ServiceName]);

            if (commandResult.IsCommandMissing)
            {
                return Result.Failure(SecretStoreErrors.StoreUnavailable);
            }

            return commandResult.ExitCode == 0
                ? Result.Success()
                : Result.Success();
        }
    }

    private sealed class LinuxSecretServiceSecretStore : ISecretStore
    {
        private readonly IProcessCommandRunner ProcessCommandRunner;

        public LinuxSecretServiceSecretStore(IProcessCommandRunner processCommandRunner)
        {
            ProcessCommandRunner = processCommandRunner ?? throw new ArgumentNullException(nameof(processCommandRunner));
        }

        public string BackendName => "linux-secret-service";
        public bool CanPersistSecrets => ProcessCommandRunner.CanStart("secret-tool");

        public Result SaveSecret(string SecretName, string SecretValue)
        {
            if (string.IsNullOrWhiteSpace(SecretName) || string.IsNullOrWhiteSpace(SecretValue))
            {
                return Result.Failure(SecretStoreErrors.InvalidRequest);
            }

            var commandResult = ProcessCommandRunner.Run(
                "secret-tool",
                ["store", $"--label=BlockiumLauncher {SecretName}", "app", "BlockiumLauncher", "secret", SecretName],
                SecretValue);

            if (commandResult.IsCommandMissing)
            {
                return Result.Failure(SecretStoreErrors.StoreUnavailable);
            }

            return commandResult.ExitCode == 0
                ? Result.Success()
                : Result.Failure(new BlockiumLauncher.Shared.Errors.Error(
                    SecretStoreErrors.PersistenceFailed.Code,
                    SecretStoreErrors.PersistenceFailed.Message,
                    commandResult.StandardError));
        }

        public Result<string> GetSecret(string SecretName)
        {
            if (string.IsNullOrWhiteSpace(SecretName))
            {
                return Result<string>.Failure(SecretStoreErrors.InvalidRequest);
            }

            var commandResult = ProcessCommandRunner.Run(
                "secret-tool",
                ["lookup", "app", "BlockiumLauncher", "secret", SecretName]);

            if (commandResult.IsCommandMissing)
            {
                return Result<string>.Failure(SecretStoreErrors.StoreUnavailable);
            }

            if (commandResult.ExitCode != 0)
            {
                return Result<string>.Failure(SecretStoreErrors.SecretNotFound);
            }

            var secretValue = commandResult.StandardOutput.Trim();
            return string.IsNullOrWhiteSpace(secretValue)
                ? Result<string>.Failure(SecretStoreErrors.SecretNotFound)
                : Result<string>.Success(secretValue);
        }

        public Result DeleteSecret(string SecretName)
        {
            if (string.IsNullOrWhiteSpace(SecretName))
            {
                return Result.Failure(SecretStoreErrors.InvalidRequest);
            }

            var commandResult = ProcessCommandRunner.Run(
                "secret-tool",
                ["clear", "app", "BlockiumLauncher", "secret", SecretName]);

            if (commandResult.IsCommandMissing)
            {
                return Result.Failure(SecretStoreErrors.StoreUnavailable);
            }

            return commandResult.ExitCode == 0
                ? Result.Success()
                : Result.Success();
        }
    }

    private sealed class UnsupportedSecretStore : ISecretStore
    {
        public string BackendName => "unsupported";
        public bool CanPersistSecrets => false;

        public Result SaveSecret(string SecretName, string SecretValue) => Result.Failure(SecretStoreErrors.StoreUnavailable);
        public Result<string> GetSecret(string SecretName) => Result<string>.Failure(SecretStoreErrors.StoreUnavailable);
        public Result DeleteSecret(string SecretName) => Result.Failure(SecretStoreErrors.StoreUnavailable);
    }
}

internal interface IProcessCommandRunner
{
    bool CanStart(string fileName);
    ProcessCommandResult Run(string fileName, IReadOnlyList<string> arguments, string? standardInput = null);
}

internal sealed record ProcessCommandResult(int ExitCode, string StandardOutput, string StandardError, bool IsCommandMissing = false);

internal sealed class ProcessCommandRunner : IProcessCommandRunner
{
    public bool CanStart(string fileName)
    {
        var result = Run(fileName, ["-h"]);
        return !result.IsCommandMissing;
    }

    public ProcessCommandResult Run(string fileName, IReadOnlyList<string> arguments, string? standardInput = null)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    RedirectStandardInput = standardInput is not null,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.Start();

            if (standardInput is not null)
            {
                process.StandardInput.Write(standardInput);
                process.StandardInput.Close();
            }

            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return new ProcessCommandResult(process.ExitCode, standardOutput, standardError);
        }
        catch (Win32Exception)
        {
            return new ProcessCommandResult(-1, string.Empty, string.Empty, IsCommandMissing: true);
        }
    }
}
