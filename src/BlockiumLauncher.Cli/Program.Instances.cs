using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Application.UseCases.Launch;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using InstallRepairInstanceRequest = BlockiumLauncher.Application.UseCases.Install.RepairInstanceRequest;

namespace BlockiumLauncher.Cli;

internal static partial class Program
{
    private static async Task<int> HandleInstancesInstallAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var name = GetRequiredOption(args, "--name");
        var version = GetOptionalOption(args, "--version");
        var loaderText = GetRequiredOption(args, "--loader");
        var loaderVersion = GetOptionalOption(args, "--loader-version");
        var targetDirectory = GetOptionalOption(args, "--path");
        var overwrite = HasFlag(args, "--overwrite");
        var downloadRuntime = HasFlag(args, "--download-runtime");
        var latest = HasFlag(args, "--latest");

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(loaderText))
        {
            WriteFailure(
                "Cli.InvalidArguments",
                "Required: --name <name> --loader <vanilla|fabric|quilt|forge|neoforge> [--version <version>] [--loader-version <version>] [--latest] [--path <directory>] [--overwrite] [--download-runtime]",
                outputJson);
            return CliExitCodes.InvalidArguments;
        }

        if (!Enum.TryParse<LoaderType>(loaderText, true, out var loaderType))
        {
            WriteFailure("Cli.InvalidArguments", $"Unknown loader type: {loaderText}", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            if (!latest)
            {
                WriteFailure("Cli.InvalidArguments", "Missing required option --version. Use --latest to auto-select the newest Minecraft release.", outputJson);
                return CliExitCodes.InvalidArguments;
            }

            var latestGameVersionResult = await ResolveLatestGameVersionAsync(serviceProvider).ConfigureAwait(false);
            if (latestGameVersionResult.IsFailure)
            {
                WriteFailure(latestGameVersionResult.Error.Code, latestGameVersionResult.Error.Message, outputJson);
                return CliExitCodes.OperationFailed;
            }

            version = latestGameVersionResult.Value;
        }

        if (loaderType == LoaderType.Vanilla)
        {
            loaderVersion = null;
        }
        else if (string.IsNullOrWhiteSpace(loaderVersion))
        {
            if (!latest)
            {
                WriteFailure("Cli.InvalidArguments", "Non-vanilla installs require --loader-version. Use --latest to auto-select the newest loader version.", outputJson);
                return CliExitCodes.InvalidArguments;
            }

            var latestLoaderVersionResult = await ResolveLatestLoaderVersionAsync(
                serviceProvider,
                loaderType,
                version).ConfigureAwait(false);

            if (latestLoaderVersionResult.IsFailure)
            {
                WriteFailure(latestLoaderVersionResult.Error.Code, latestLoaderVersionResult.Error.Message, outputJson);
                return CliExitCodes.OperationFailed;
            }

            loaderVersion = latestLoaderVersionResult.Value;
        }

        Console.WriteLine("Install started. Logs are being written to %APPDATALOCAL%\\BlockiumLauncher\\logs.");

        var useCase = serviceProvider.GetRequiredService<InstallInstanceUseCase>();
        var result = await useCase.ExecuteAsync(new InstallInstanceRequest
        {
            InstanceName = name,
            GameVersion = version,
            LoaderType = loaderType,
            LoaderVersion = loaderVersion,
            TargetDirectory = targetDirectory,
            OverwriteIfExists = overwrite,
            DownloadRuntime = downloadRuntime
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        var payload = new
        {
            InstanceId = result.Value.Instance.InstanceId.ToString(),
            result.Value.Instance.Name,
            GameVersion = result.Value.Instance.GameVersion.ToString(),
            result.Value.Instance.LoaderType,
            LoaderVersion = result.Value.Instance.LoaderVersion?.ToString(),
            result.Value.InstalledPath
        };

        WriteSuccess(payload, outputJson, lines =>
        {
            lines.Add($"Instance installed: {payload.Name} ({payload.InstanceId})");
            lines.Add($"GameVersion: {payload.GameVersion}");
            lines.Add($"Loader: {payload.LoaderType}");
            lines.Add($"LoaderVersion: {payload.LoaderVersion ?? ""}");
            lines.Add($"InstalledPath: {payload.InstalledPath}");
        });

        return CliExitCodes.Success;
    }

    private static async Task<int> HandleInstancesVerifyAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var instanceIdText = GetRequiredOption(args, "--instance-id");
        if (string.IsNullOrWhiteSpace(instanceIdText))
        {
            WriteFailure("Cli.InvalidArguments", "Missing required option --instance-id.", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        var useCase = serviceProvider.GetRequiredService<VerifyInstanceFilesUseCase>();
        var result = await useCase.ExecuteAsync(new VerifyInstanceFilesRequest
        {
            InstanceId = new InstanceId(instanceIdText)
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        var payload = new
        {
            InstanceId = result.Value.Instance.InstanceId.ToString(),
            result.Value.Instance.Name,
            result.Value.IsValid,
            Issues = result.Value.Issues.Select(x => new
            {
                Kind = x.Kind.ToString(),
                x.Path,
                x.Message
            }).ToArray()
        };

        WriteSuccess(payload, outputJson, lines =>
        {
            lines.Add($"Instance: {payload.Name} ({payload.InstanceId})");
            lines.Add($"Valid: {payload.IsValid}");

            foreach (var issue in payload.Issues)
            {
                lines.Add($"- {issue.Kind}: {issue.Path} | {issue.Message}");
            }
        });

        return CliExitCodes.Success;
    }

    private static async Task<int> HandleInstancesRepairAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var instanceIdText = GetRequiredOption(args, "--instance-id");
        if (string.IsNullOrWhiteSpace(instanceIdText))
        {
            WriteFailure("Cli.InvalidArguments", "Missing required option --instance-id.", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        var useCase = serviceProvider.GetRequiredService<RepairInstanceUseCase>();
        var result = await useCase.ExecuteAsync(new InstallRepairInstanceRequest
        {
            InstanceId = new InstanceId(instanceIdText)
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        var payload = new
        {
            InstanceId = result.Value.Instance.InstanceId.ToString(),
            result.Value.Instance.Name,
            result.Value.Changed,
            result.Value.RepairedPaths,
            Verification = new
            {
                result.Value.Verification.IsValid,
                Issues = result.Value.Verification.Issues.Select(x => new
                {
                    Kind = x.Kind.ToString(),
                    x.Path,
                    x.Message
                }).ToArray()
            }
        };

        WriteSuccess(payload, outputJson, lines =>
        {
            lines.Add($"Instance repaired: {payload.Name} ({payload.InstanceId})");
            lines.Add($"Changed: {payload.Changed}");

            foreach (var path in payload.RepairedPaths)
            {
                lines.Add($"- repaired: {path}");
            }

            lines.Add($"Valid after repair: {payload.Verification.IsValid}");
        });

        return CliExitCodes.Success;
    }

    private static async Task<int> HandleInstancesStartAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var instanceIdText = GetOptionalOption(args, "--instance-id");
        var instanceName = GetOptionalOption(args, "--name");
        var accountIdText = GetOptionalOption(args, "--account-id");

        if (string.IsNullOrWhiteSpace(instanceIdText) && string.IsNullOrWhiteSpace(instanceName))
        {
            WriteFailure("Cli.InvalidArguments", "Required: --instance-id <id> or --name <instance-name>.", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        var instanceRepository = serviceProvider.GetRequiredService<IInstanceRepository>();
        var javaRuntimeResolver = serviceProvider.GetRequiredService<IJavaRuntimeResolver>();

        var instance = !string.IsNullOrWhiteSpace(instanceIdText)
            ? await instanceRepository.GetByIdAsync(new InstanceId(instanceIdText), CancellationToken.None).ConfigureAwait(false)
            : await instanceRepository.GetByNameAsync(instanceName!, CancellationToken.None).ConfigureAwait(false);

        if (instance is null)
        {
            WriteFailure("Launch.InstanceNotFound", "The requested instance was not found.", outputJson);
            return CliExitCodes.OperationFailed;
        }

        var javaResult = await javaRuntimeResolver.ResolveExecutablePathAsync(
            instance.GameVersion.ToString(),
            instance.LoaderType,
            instance.LaunchProfile.PreferredJavaMajor,
            instance.LaunchProfile.SkipCompatibilityChecks,
            CancellationToken.None).ConfigureAwait(false);
        if (javaResult.IsFailure)
        {
            WriteFailure(javaResult.Error.Code, javaResult.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        Console.WriteLine("Launch started. Logs are being written to launcher logs.");

        var useCase = serviceProvider.GetRequiredService<LaunchInstanceUseCase>();
        var result = await useCase.ExecuteAsync(new LaunchInstanceRequest
        {
            InstanceId = instance.InstanceId,
            AccountId = string.IsNullOrWhiteSpace(accountIdText) ? null : new AccountId(accountIdText),
            JavaExecutablePath = javaResult.Value,
            MainClass = string.Empty,
            AssetsDirectory = null,
            AssetIndexId = null,
            ClasspathEntries = []
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        var payload = new
        {
            result.Value.LaunchId,
            result.Value.InstanceId,
            result.Value.ProcessId,
            result.Value.IsRunning,
            result.Value.HasExited,
            result.Value.ExitCode,
            JavaPath = javaResult.Value
        };

        WriteSuccess(payload, outputJson, lines =>
        {
            lines.Add($"LaunchId: {payload.LaunchId}");
            lines.Add($"InstanceId: {payload.InstanceId}");
            lines.Add($"ProcessId: {payload.ProcessId}");
            lines.Add($"Running: {payload.IsRunning}");
            lines.Add($"HasExited: {payload.HasExited}");
            lines.Add($"ExitCode: {payload.ExitCode}");
            lines.Add($"Java: {payload.JavaPath}");
        });

        return CliExitCodes.Success;
    }
}
