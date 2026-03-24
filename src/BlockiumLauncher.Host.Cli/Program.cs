using System.Text.Json;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Accounts;
using BlockiumLauncher.Application.UseCases.Catalog;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Application.UseCases.Instances;
using BlockiumLauncher.Application.UseCases.Launch;
using BlockiumLauncher.Contracts.Launch;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Composition;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;
using Microsoft.Extensions.DependencyInjection;
using InstallRepairInstanceRequest = BlockiumLauncher.Application.UseCases.Install.RepairInstanceRequest;

namespace BlockiumLauncher.Host.Cli;

internal static class Program
{
    private delegate Task<int> CliCommandHandler(IServiceProvider serviceProvider, string[] args, bool outputJson);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly IReadOnlyDictionary<string, CliCommandHandler> CommandHandlers =
        new Dictionary<string, CliCommandHandler>(StringComparer.OrdinalIgnoreCase)
        {
            ["accounts list"] = static (serviceProvider, _, outputJson) => HandleAccountsListAsync(serviceProvider, outputJson),
            ["accounts add-offline"] = static (serviceProvider, args, outputJson) => HandleAccountsAddOfflineAsync(serviceProvider, args, outputJson),
            ["accounts set-default"] = static (serviceProvider, args, outputJson) => HandleAccountsSetDefaultAsync(serviceProvider, args, outputJson),
            ["accounts remove"] = static (serviceProvider, args, outputJson) => HandleAccountsRemoveAsync(serviceProvider, args, outputJson),
            ["instances install"] = static (serviceProvider, args, outputJson) => HandleInstancesInstallAsync(serviceProvider, args, outputJson),
            ["instances verify"] = static (serviceProvider, args, outputJson) => HandleInstancesVerifyAsync(serviceProvider, args, outputJson),
            ["instances repair"] = static (serviceProvider, args, outputJson) => HandleInstancesRepairAsync(serviceProvider, args, outputJson),
            ["instances start"] = static (serviceProvider, args, outputJson) => HandleInstancesStartAsync(serviceProvider, args, outputJson),
            ["launch plan"] = static (serviceProvider, args, outputJson) => HandleLaunchPlanAsync(serviceProvider, args, outputJson),
            ["launch run"] = static (serviceProvider, args, outputJson) => HandleLaunchRunAsync(serviceProvider, args, outputJson),
            ["launch status"] = static (serviceProvider, args, outputJson) => HandleLaunchStatusAsync(serviceProvider, args, outputJson),
            ["launch stop"] = static (serviceProvider, args, outputJson) => HandleLaunchStopAsync(serviceProvider, args, outputJson),
            ["catalog mods"] = static (serviceProvider, args, outputJson) => HandleCatalogSearchAsync(serviceProvider, CatalogContentType.Mod, args, outputJson),
            ["catalog modpacks"] = static (serviceProvider, args, outputJson) => HandleCatalogSearchAsync(serviceProvider, CatalogContentType.Modpack, args, outputJson),
            ["catalog resourcepacks"] = static (serviceProvider, args, outputJson) => HandleCatalogSearchAsync(serviceProvider, CatalogContentType.ResourcePack, args, outputJson),
            ["catalog shaders"] = static (serviceProvider, args, outputJson) => HandleCatalogSearchAsync(serviceProvider, CatalogContentType.Shader, args, outputJson),
            ["versions vanilla"] = static (serviceProvider, args, outputJson) => HandleVersionsVanillaAsync(serviceProvider, args, outputJson),
            ["versions loaders"] = static (serviceProvider, args, outputJson) => HandleVersionsLoadersAsync(serviceProvider, args, outputJson),
            ["diagnostics dump"] = static (serviceProvider, args, outputJson) => HandleDiagnosticsDumpAsync(serviceProvider, args, outputJson),
            ["instance content list"] = static (serviceProvider, args, outputJson) => HandleInstanceContentListAsync(serviceProvider, args, outputJson),
            ["instance content rescan"] = static (serviceProvider, args, outputJson) => HandleInstanceContentRescanAsync(serviceProvider, args, outputJson),
            ["instance mods disable"] = static (serviceProvider, args, outputJson) => HandleInstanceModEnabledAsync(serviceProvider, args, enabled: false, outputJson),
            ["instance mods enable"] = static (serviceProvider, args, outputJson) => HandleInstanceModEnabledAsync(serviceProvider, args, enabled: true, outputJson)
        };

    public static async Task<int> Main(string[] args)
    {
        var outputJson = args.Any(x => string.Equals(x, "--json", StringComparison.OrdinalIgnoreCase));

        try
        {
            var filteredArgs = args.Where(x => !string.Equals(x, "--json", StringComparison.OrdinalIgnoreCase)).ToArray();

            var services = new ServiceCollection();
            services.AddBlockiumLauncherInfrastructure();

            await using var serviceProvider = services.BuildServiceProvider();

            if (filteredArgs.Length == 0)
            {
                WriteHelp(outputJson);
                return CliExitCodes.InvalidArguments;
            }

            return await DispatchAsync(serviceProvider, filteredArgs, outputJson).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            WriteFailure("UnhandledError", ex.Message, outputJson);
            return CliExitCodes.UnhandledError;
        }
    }

    private static async Task<int> DispatchAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        if (args.Length < 2)
        {
            WriteHelp(outputJson);
            return CliExitCodes.InvalidArguments;
        }

        if (TryResolveCommandHandler(args, out var handler, out var consumedSegments))
        {
            return await handler(serviceProvider, args.Skip(consumedSegments).ToArray(), outputJson).ConfigureAwait(false);
        }

        WriteHelp(outputJson);
        return CliExitCodes.InvalidArguments;
    }

    private static bool TryResolveCommandHandler(string[] args, out CliCommandHandler handler, out int consumedSegments)
    {
        for (var segmentCount = Math.Min(3, args.Length); segmentCount >= 2; segmentCount--)
        {
            var commandKey = string.Join(" ", args.Take(segmentCount));
            if (CommandHandlers.TryGetValue(commandKey, out handler!))
            {
                consumedSegments = segmentCount;
                return true;
            }
        }

        handler = default!;
        consumedSegments = 0;
        return false;
    }

    private static async Task<int> HandleAccountsListAsync(IServiceProvider serviceProvider, bool outputJson)
    {
        var useCase = serviceProvider.GetRequiredService<ListAccountsUseCase>();
        var result = await useCase.ExecuteAsync().ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        var payload = result.Value.Select(account => new
        {
            AccountId = account.AccountId.ToString(),
            account.Provider,
            account.Username,
            account.AccountIdentifier,
            account.IsDefault,
            account.State
        }).ToArray();

        WriteSuccess(payload, outputJson, lines =>
        {
            if (payload.Length == 0)
            {
                lines.Add("No accounts found.");
                return;
            }

            foreach (var account in payload)
            {
                lines.Add($"{account.AccountId} | {account.Provider} | {account.Username} | Default={account.IsDefault} | State={account.State}");
            }
        });

        return CliExitCodes.Success;
    }

    private static async Task<int> HandleAccountsAddOfflineAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var username = GetRequiredOption(args, "--username");
        if (string.IsNullOrWhiteSpace(username))
        {
            WriteFailure("Cli.InvalidArguments", "Missing required option --username.", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        var setAsDefault = HasFlag(args, "--set-default");

        var useCase = serviceProvider.GetRequiredService<AddAccountUseCase>();
        var result = await useCase.ExecuteAsync(new AddAccountRequest
        {
            Provider = AccountProvider.Offline,
            Username = username,
            SetAsDefault = setAsDefault
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        var payload = new
        {
            AccountId = result.Value.AccountId.ToString(),
            result.Value.Provider,
            result.Value.Username,
            result.Value.IsDefault,
            result.Value.State
        };

        WriteSuccess(payload, outputJson, lines =>
        {
            lines.Add("Offline account added.");
            lines.Add($"{payload.AccountId} | {payload.Username} | Default={payload.IsDefault} | State={payload.State}");
        });

        return CliExitCodes.Success;
    }

    private static async Task<int> HandleAccountsSetDefaultAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var accountIdText = GetRequiredOption(args, "--account-id");
        if (string.IsNullOrWhiteSpace(accountIdText))
        {
            WriteFailure("Cli.InvalidArguments", "Missing required option --account-id.", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        var useCase = serviceProvider.GetRequiredService<SetDefaultAccountUseCase>();
        var result = await useCase.ExecuteAsync(new SetDefaultAccountRequest
        {
            AccountId = new AccountId(accountIdText)
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        WriteSuccess(new { AccountId = accountIdText }, outputJson, lines =>
        {
            lines.Add($"Default account set: {accountIdText}");
        });

        return CliExitCodes.Success;
    }

    private static async Task<int> HandleAccountsRemoveAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var accountIdText = GetRequiredOption(args, "--account-id");
        if (string.IsNullOrWhiteSpace(accountIdText))
        {
            WriteFailure("Cli.InvalidArguments", "Missing required option --account-id.", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        var useCase = serviceProvider.GetRequiredService<RemoveAccountUseCase>();
        var result = await useCase.ExecuteAsync(new RemoveAccountRequest
        {
            AccountId = new AccountId(accountIdText)
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        WriteSuccess(new { AccountId = accountIdText }, outputJson, lines =>
        {
            lines.Add($"Account removed: {accountIdText}");
        });

        return CliExitCodes.Success;
    }

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

        var javaResult = await javaRuntimeResolver.ResolveExecutablePathAsync(instance.GameVersion.ToString(), CancellationToken.None).ConfigureAwait(false);
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
    
private static async Task<int> HandleInstancesRenameAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
{
    var instanceIdText = GetOptionalOption(args, "--instance-id");
    var currentName = GetOptionalOption(args, "--name");
    var newName = GetRequiredOption(args, "--new-name");

    if ((string.IsNullOrWhiteSpace(instanceIdText) && string.IsNullOrWhiteSpace(currentName)) || string.IsNullOrWhiteSpace(newName))
    {
        WriteFailure("Cli.InvalidArguments", "Required: (--instance-id <id> or --name <name>) --new-name <name>.", outputJson);
        return CliExitCodes.InvalidArguments;
    }

    var instanceRepository = serviceProvider.GetRequiredService<IInstanceRepository>();

    var instance = !string.IsNullOrWhiteSpace(instanceIdText)
        ? await instanceRepository.GetByIdAsync(new InstanceId(instanceIdText), CancellationToken.None).ConfigureAwait(false)
        : await instanceRepository.GetByNameAsync(currentName!, CancellationToken.None).ConfigureAwait(false);

    if (instance is null)
    {
        WriteFailure("Instances.NotFound", "The requested instance was not found.", outputJson);
        return CliExitCodes.OperationFailed;
    }

    var conflictingInstance = await instanceRepository.GetByNameAsync(newName, CancellationToken.None).ConfigureAwait(false);
    if (conflictingInstance is not null && conflictingInstance.InstanceId.ToString() != instance.InstanceId.ToString())
    {
        WriteFailure("Instances.NameAlreadyExists", "Another instance with the requested name already exists.", outputJson);
        return CliExitCodes.OperationFailed;
    }

    instance.Rename(newName);
    await instanceRepository.SaveAsync(instance, CancellationToken.None).ConfigureAwait(false);

    var payload = new
    {
        InstanceId = instance.InstanceId.ToString(),
        instance.Name,
        instance.InstallLocation
    };

    WriteSuccess(payload, outputJson, lines =>
    {
        lines.Add($"Instance renamed: {payload.Name} ({payload.InstanceId})");
        lines.Add($"InstalledPath: {payload.InstallLocation}");
    });

    return CliExitCodes.Success;
}

private static async Task<int> HandleInstancesDeleteAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
{
    var instanceIdText = GetOptionalOption(args, "--instance-id");
    var instanceName = GetOptionalOption(args, "--name");
    var deleteFiles = HasFlag(args, "--delete-files");

    if (string.IsNullOrWhiteSpace(instanceIdText) && string.IsNullOrWhiteSpace(instanceName))
    {
        WriteFailure("Cli.InvalidArguments", "Required: --instance-id <id> or --name <name>.", outputJson);
        return CliExitCodes.InvalidArguments;
    }

    var instanceRepository = serviceProvider.GetRequiredService<IInstanceRepository>();

    var instance = !string.IsNullOrWhiteSpace(instanceIdText)
        ? await instanceRepository.GetByIdAsync(new InstanceId(instanceIdText), CancellationToken.None).ConfigureAwait(false)
        : await instanceRepository.GetByNameAsync(instanceName!, CancellationToken.None).ConfigureAwait(false);

    if (instance is null)
    {
        WriteFailure("Instances.NotFound", "The requested instance was not found.", outputJson);
        return CliExitCodes.OperationFailed;
    }

    var installLocation = instance.InstallLocation;
    instance.MarkDeleted();
    await instanceRepository.DeleteAsync(instance.InstanceId, CancellationToken.None).ConfigureAwait(false);

    if (deleteFiles && !string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
    {
        Directory.Delete(installLocation, true);
    }

    var payload = new
    {
        InstanceId = instance.InstanceId.ToString(),
        instance.Name,
        InstallLocation = installLocation,
        DeletedFiles = deleteFiles
    };

    WriteSuccess(payload, outputJson, lines =>
    {
        lines.Add($"Instance deleted: {payload.Name} ({payload.InstanceId})");
        lines.Add($"DeletedFiles: {payload.DeletedFiles}");
        lines.Add($"InstalledPath: {payload.InstallLocation}");
    });

    return CliExitCodes.Success;
}
private static async Task<int> HandleLaunchPlanAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var buildResult = await BuildLaunchPlanAsync(serviceProvider, args).ConfigureAwait(false);
        if (buildResult.IsFailure)
        {
            WriteFailure(buildResult.ErrorCode!, buildResult.ErrorMessage!, outputJson);
            return buildResult.ExitCode;
        }

        LaunchPlanDto plan = buildResult.Plan!;

        WriteSuccess(plan, outputJson, lines =>
        {
            lines.Add($"InstanceId: {plan.InstanceId}");
            lines.Add($"AccountId: {plan.AccountId}");
            lines.Add($"Java: {plan.JavaExecutablePath}");
            lines.Add($"WorkDir: {plan.WorkingDirectory}");
            lines.Add($"MainClass: {plan.MainClass}");
            lines.Add($"DryRun: {plan.IsDryRun}");
            lines.Add("JVM Args:");
            foreach (var arg in plan.JvmArguments)
            {
                lines.Add($"  {arg.Value}");
            }

            lines.Add("Game Args:");
            foreach (var arg in plan.GameArguments)
            {
                lines.Add($"  {arg.Value}");
            }
        });

        return CliExitCodes.Success;
    }

    private static async Task<int> HandleLaunchRunAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var instanceIdText = GetRequiredOption(args, "--instance-id");
        var javaPath = GetRequiredOption(args, "--java");
        var mainClass = GetOptionalOption(args, "--main-class") ?? string.Empty;
        var accountIdText = GetOptionalOption(args, "--account-id");
        var assetsDir = GetOptionalOption(args, "--assets-dir");
        var assetIndex = GetOptionalOption(args, "--asset-index");
        var classpathEntries = GetMultiOption(args, "--classpath");

        if (string.IsNullOrWhiteSpace(instanceIdText) || string.IsNullOrWhiteSpace(javaPath))
        {
            WriteFailure(
                "Cli.InvalidArguments",
                "Required: --instance-id <id> --java <path>. Optional overrides: --main-class, --classpath, --assets-dir, --asset-index, --account-id",
                outputJson);
            return CliExitCodes.InvalidArguments;
        }

        Console.WriteLine("Launch started. Logs are being written to %APPDATA%\\BlockiumLauncher\\logs.");

        var useCase = serviceProvider.GetRequiredService<LaunchInstanceUseCase>();
        var result = await useCase.ExecuteAsync(new LaunchInstanceRequest
        {
            InstanceId = new InstanceId(instanceIdText),
            AccountId = string.IsNullOrWhiteSpace(accountIdText) ? null : new AccountId(accountIdText),
            JavaExecutablePath = javaPath,
            MainClass = mainClass,
            AssetsDirectory = assetsDir,
            AssetIndexId = assetIndex,
            ClasspathEntries = classpathEntries
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
            OutputLines = result.Value.OutputLines.Select(x => new
            {
                x.TimestampUtc,
                x.Stream,
                x.Message
            }).ToArray()
        };

        WriteSuccess(payload, outputJson, lines =>
        {
            lines.Add($"LaunchId: {payload.LaunchId}");
            lines.Add($"InstanceId: {payload.InstanceId}");
            lines.Add($"ProcessId: {payload.ProcessId}");
            lines.Add($"Running: {payload.IsRunning}");
            lines.Add($"HasExited: {payload.HasExited}");
            lines.Add($"ExitCode: {payload.ExitCode}");
            if (payload.OutputLines.Length > 0)
            {
                lines.Add("Output:");
                foreach (var line in payload.OutputLines)
                {
                    lines.Add($"  [{line.Stream}] {line.Message}");
                }
            }
        });

        return CliExitCodes.Success;
    }

    private static async Task<int> HandleLaunchStatusAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var launchIdText = GetRequiredOption(args, "--launch-id");
        if (!Guid.TryParse(launchIdText, out var launchId))
        {
            WriteFailure("Cli.InvalidArguments", "Missing or invalid required option --launch-id.", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        var useCase = serviceProvider.GetRequiredService<GetLaunchStatusUseCase>();
        var result = await useCase.ExecuteAsync(new GetLaunchStatusRequest
        {
            LaunchId = launchId
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
            OutputLines = result.Value.OutputLines.Select(x => new
            {
                x.TimestampUtc,
                x.Stream,
                x.Message
            }).ToArray()
        };

        WriteSuccess(payload, outputJson, lines =>
        {
            lines.Add($"LaunchId: {payload.LaunchId}");
            lines.Add($"InstanceId: {payload.InstanceId}");
            lines.Add($"ProcessId: {payload.ProcessId}");
            lines.Add($"Running: {payload.IsRunning}");
            lines.Add($"HasExited: {payload.HasExited}");
            lines.Add($"ExitCode: {payload.ExitCode}");
            if (payload.OutputLines.Length > 0)
            {
                lines.Add("Output:");
                foreach (var line in payload.OutputLines)
                {
                    lines.Add($"  [{line.Stream}] {line.Message}");
                }
            }
        });

        return CliExitCodes.Success;
    }

    private static async Task<int> HandleLaunchStopAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var launchIdText = GetRequiredOption(args, "--launch-id");
        if (!Guid.TryParse(launchIdText, out var launchId))
        {
            WriteFailure("Cli.InvalidArguments", "Missing or invalid required option --launch-id.", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        var useCase = serviceProvider.GetRequiredService<StopLaunchUseCase>();
        var result = await useCase.ExecuteAsync(new StopLaunchRequest
        {
            LaunchId = launchId
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        WriteSuccess(new { LaunchId = launchId }, outputJson, lines =>
        {
            lines.Add($"Launch stopped: {launchId}");
        });

        return CliExitCodes.Success;
    }

    private static async Task<int> HandleCatalogSearchAsync(
        IServiceProvider serviceProvider,
        CatalogContentType contentType,
        string[] args,
        bool outputJson)
    {
        var providerText = GetOptionalOption(args, "--provider") ?? "modrinth";
        var loader = GetOptionalOption(args, "--loader");
        var gameVersion = GetOptionalOption(args, "--game-version");
        var query = GetOptionalOption(args, "--query");
        var categories = GetMultiOption(args, "--category");

        if (!TryParseCatalogProvider(providerText, out var provider))
        {
            WriteFailure("Cli.InvalidArguments", $"Unsupported provider: {providerText}", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        if (!TryParseCatalogSearchSort(GetOptionalOption(args, "--sort"), out var sort))
        {
            WriteFailure("Cli.InvalidArguments", "Unsupported sort. Use relevance, downloads, follows, newest, or updated.", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        if (!TryParseInt32(GetOptionalOption(args, "--limit"), 20, 1, 100, out var limit) ||
            !TryParseInt32(GetOptionalOption(args, "--offset"), 0, 0, int.MaxValue, out var offset))
        {
            WriteFailure("Cli.InvalidArguments", "Invalid paging options. --limit must be 1-100 and --offset must be 0 or greater.", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        var useCase = serviceProvider.GetRequiredService<SearchCatalogUseCase>();
        var result = await useCase.ExecuteAsync(new SearchCatalogRequest
        {
            Provider = provider,
            ContentType = contentType,
            Query = query,
            GameVersion = gameVersion,
            Loader = loader,
            Categories = categories,
            Sort = sort,
            Limit = limit,
            Offset = offset
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        WriteSuccess(result.Value, outputJson, lines =>
        {
            if (result.Value.Count == 0)
            {
                lines.Add($"No matching {contentType.ToString().ToLowerInvariant()} results found.");
                return;
            }

            foreach (var item in result.Value)
            {
                lines.Add($"{item.Title} | {item.ProjectId} | Downloads={item.Downloads} | Loaders={string.Join(",", item.Loaders)} | Versions={string.Join(",", item.GameVersions)}");
                if (!string.IsNullOrWhiteSpace(item.ProjectUrl))
                {
                    lines.Add(item.ProjectUrl!);
                }
            }
        });

        return CliExitCodes.Success;
    }

    private static async Task<int> HandleVersionsVanillaAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var service = serviceProvider.GetRequiredService<IVersionManifestService>();
        var result = await service.GetAvailableVersionsAsync(CancellationToken.None).ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        var requestedTypes = GetMultiOption(args, "--type")
            .Select(NormalizeVanillaType)
            .Where(x => x is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var latestOnly = HasFlag(args, "--latest");

        var filtered = result.Value
            .Where(version => requestedTypes.Length == 0 || MatchesVanillaType(version, requestedTypes))
            .OrderByDescending(version => version.ReleasedAtUtc)
            .ToList();

        if (latestOnly && filtered.Count > 1)
        {
            filtered = [filtered[0]];
        }

        var payload = filtered.ToArray();

        WriteSuccess(payload, outputJson, lines =>
        {
            if (payload.Length == 0)
            {
                lines.Add("No vanilla versions matched the requested filter.");
                return;
            }

            foreach (var item in payload)
            {
                lines.Add(JsonSerializer.Serialize(item, JsonOptions));
            }
        });

        return CliExitCodes.Success;
    }

        private static async Task<int> HandleVersionsLoadersAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var loaderText = GetRequiredOption(args, "--loader");
        var gameVersionText = GetOptionalOption(args, "--game-version");
        var latestOnly = HasFlag(args, "--latest");

        if (string.IsNullOrWhiteSpace(loaderText))
        {
            WriteFailure("Cli.InvalidArguments", "Required: --loader <fabric|quilt|forge|neoforge|vanilla> [--game-version <version>] [--latest]", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        if (!Enum.TryParse<LoaderType>(loaderText, true, out var loaderType))
        {
            WriteFailure("Cli.InvalidArguments", $"Unknown loader type: {loaderText}", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        if (loaderType == LoaderType.Vanilla)
        {
            WriteFailure("Cli.InvalidArguments", "Use `versions vanilla` for vanilla Minecraft versions.", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        if (string.IsNullOrWhiteSpace(gameVersionText))
        {
            if (!latestOnly)
            {
                WriteFailure("Cli.InvalidArguments", "Required: --loader <fabric|quilt|forge|neoforge> --game-version <version>. Use --latest to auto-select the newest Minecraft release.", outputJson);
                return CliExitCodes.InvalidArguments;
            }

            var latestGameVersionResult = await ResolveLatestGameVersionAsync(serviceProvider).ConfigureAwait(false);
            if (latestGameVersionResult.IsFailure)
            {
                WriteFailure(latestGameVersionResult.Error.Code, latestGameVersionResult.Error.Message, outputJson);
                return CliExitCodes.OperationFailed;
            }

            gameVersionText = latestGameVersionResult.Value;
        }

        var service = serviceProvider.GetRequiredService<ILoaderMetadataService>();
        var result = await service.GetLoaderVersionsAsync(
            loaderType,
            new VersionId(gameVersionText),
            CancellationToken.None).ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        var payload = result.Value
            .OrderByDescending(x => x.IsStable)
            .ThenByDescending(x => x.LoaderVersion, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(x => CompareVersionStrings(x.LoaderVersion, "0"))
            .ToArray();

        if (latestOnly && payload.Length > 1)
        {
            payload = [payload[0]];
        }

        WriteSuccess(payload, outputJson, lines =>
        {
            if (payload.Length == 0)
            {
                lines.Add($"No loader versions returned for {loaderType} on {gameVersionText}.");
                return;
            }

            foreach (var item in payload)
            {
                lines.Add(JsonSerializer.Serialize(item, JsonOptions));
            }
        });

        return CliExitCodes.Success;
    }

    private static async Task<int> HandleDiagnosticsDumpAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var launcherPaths = serviceProvider.GetRequiredService<ILauncherPaths>();
        var outputPath = GetOptionalOption(args, "--output");
        var dumpDirectory = launcherPaths.DiagnosticsDirectory;

        Directory.CreateDirectory(dumpDirectory);

        var resolvedOutputPath = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(dumpDirectory, "dump-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json")
            : Path.GetFullPath(outputPath);

        var logsDirectory = launcherPaths.LogsDirectory;

        Directory.CreateDirectory(logsDirectory);

        var latestLogFile = File.Exists(launcherPaths.LatestLogFilePath)
            ? launcherPaths.LatestLogFilePath
            : null;

        var recentLogLines = latestLogFile is null
            ? Array.Empty<string>()
            : File.ReadAllLines(latestLogFile).TakeLast(200).ToArray();

        var payload = new
        {
            CreatedAtUtc = DateTimeOffset.UtcNow,
            AppDataRoot = launcherPaths.RootDirectory,
            LogsDirectory = logsDirectory,
            LatestLogFile = latestLogFile,
            RecentLogLines = recentLogLines
        };

        await File.WriteAllTextAsync(
            resolvedOutputPath,
            JsonSerializer.Serialize(payload, JsonOptions)).ConfigureAwait(false);

        WriteSuccess(new { OutputPath = resolvedOutputPath }, outputJson, lines =>
        {
            lines.Add("Diagnostics dump written.");
            lines.Add(resolvedOutputPath);
        });

        return CliExitCodes.Success;
    }

    private static async Task<int> HandleInstanceContentListAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var instanceIdText = GetRequiredOption(args, "--instance-id");
        if (string.IsNullOrWhiteSpace(instanceIdText))
        {
            WriteFailure("Cli.InvalidArguments", "Missing required option --instance-id.", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        var useCase = serviceProvider.GetRequiredService<ListInstanceContentUseCase>();
        var result = await useCase.ExecuteAsync(new ListInstanceContentRequest
        {
            InstanceId = new InstanceId(instanceIdText)
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        return WriteInstanceContentMetadata(result.Value, outputJson);
    }

    private static async Task<int> HandleInstanceContentRescanAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var instanceIdText = GetRequiredOption(args, "--instance-id");
        if (string.IsNullOrWhiteSpace(instanceIdText))
        {
            WriteFailure("Cli.InvalidArguments", "Missing required option --instance-id.", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        var useCase = serviceProvider.GetRequiredService<RescanInstanceContentUseCase>();
        var result = await useCase.ExecuteAsync(new RescanInstanceContentRequest
        {
            InstanceId = new InstanceId(instanceIdText)
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        return WriteInstanceContentMetadata(result.Value, outputJson);
    }

    private static async Task<int> HandleInstanceModEnabledAsync(IServiceProvider serviceProvider, string[] args, bool enabled, bool outputJson)
    {
        var instanceIdText = GetRequiredOption(args, "--instance-id");
        var modReference = GetRequiredOption(args, "--mod");

        if (string.IsNullOrWhiteSpace(instanceIdText) || string.IsNullOrWhiteSpace(modReference))
        {
            WriteFailure("Cli.InvalidArguments", "Required: --instance-id <id> --mod <name-or-relative-path>.", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        var useCase = serviceProvider.GetRequiredService<SetModEnabledUseCase>();
        var result = await useCase.ExecuteAsync(new SetModEnabledRequest
        {
            InstanceId = new InstanceId(instanceIdText),
            ModReference = modReference,
            Enabled = enabled
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        return WriteInstanceContentMetadata(result.Value, outputJson);
    }

    private static async Task<(bool IsFailure, int ExitCode, string? ErrorCode, string? ErrorMessage, LaunchPlanDto? Plan)> BuildLaunchPlanAsync(IServiceProvider serviceProvider, string[] args)
    {
        var instanceIdText = GetRequiredOption(args, "--instance-id");
        var javaPath = GetRequiredOption(args, "--java");
        var mainClass = GetOptionalOption(args, "--main-class") ?? string.Empty;
        var accountIdText = GetOptionalOption(args, "--account-id");
        var assetsDir = GetOptionalOption(args, "--assets-dir");
        var assetIndex = GetOptionalOption(args, "--asset-index");
        var classpathEntries = GetMultiOption(args, "--classpath");

        if (string.IsNullOrWhiteSpace(instanceIdText) || string.IsNullOrWhiteSpace(javaPath))
        {
            return (true, CliExitCodes.InvalidArguments, "Cli.InvalidArguments", "Required: --instance-id <id> --java <path>. Optional overrides: --main-class, --classpath, --assets-dir, --asset-index, --account-id", null);
        }

        var useCase = serviceProvider.GetRequiredService<BuildLaunchPlanUseCase>();
        var result = await useCase.ExecuteAsync(new BuildLaunchPlanRequest
        {
            InstanceId = new InstanceId(instanceIdText),
            AccountId = string.IsNullOrWhiteSpace(accountIdText) ? null : new AccountId(accountIdText),
            JavaExecutablePath = javaPath,
            MainClass = mainClass,
            AssetsDirectory = assetsDir,
            AssetIndexId = assetIndex,
            ClasspathEntries = classpathEntries,
            IsDryRun = true
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return (true, CliExitCodes.OperationFailed, result.Error.Code, result.Error.Message, null);
        }

        return (false, CliExitCodes.Success, null, null, result.Value);
    }

    private static async Task<Result<string>> ResolveLatestGameVersionAsync(IServiceProvider serviceProvider)
    {
        var service = serviceProvider.GetRequiredService<IVersionManifestService>();
        var result = await service.GetAvailableVersionsAsync(CancellationToken.None).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<string>.Failure(result.Error);
        }

        var latestRelease = result.Value
            .Where(x => x.IsRelease)
            .OrderByDescending(x => x.ReleasedAtUtc)
            .FirstOrDefault();

        if (latestRelease is null)
        {
            return Result<string>.Failure(new Error(
                "Cli.LatestGameVersionNotFound",
                "Could not resolve the latest Minecraft release version."));
        }

        return Result<string>.Success(latestRelease.VersionId.ToString());
    }

    private static async Task<Result<string>> ResolveLatestLoaderVersionAsync(
        IServiceProvider serviceProvider,
        LoaderType loaderType,
        string gameVersion)
    {
        var service = serviceProvider.GetRequiredService<ILoaderMetadataService>();
        var result = await service.GetLoaderVersionsAsync(
            loaderType,
            new VersionId(gameVersion),
            CancellationToken.None).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result<string>.Failure(result.Error);
        }

        var latestLoaderVersion = result.Value
            .OrderByDescending(x => x.IsStable)
            .ThenByDescending(x => NormalizeComparableVersion(x.LoaderVersion), StringComparer.Ordinal)
            .ThenByDescending(x => x.LoaderVersion, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.LoaderVersion)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(latestLoaderVersion))
        {
            return Result<string>.Failure(new Error(
                "Cli.LatestLoaderVersionNotFound",
                $"Could not resolve the latest loader version for {loaderType} on Minecraft {gameVersion}."));
        }

        return Result<string>.Success(latestLoaderVersion);
    }

    private static int CompareVersionStrings(string? left, string? right)
    {
        return string.Compare(
            NormalizeComparableVersion(left),
            NormalizeComparableVersion(right),
            StringComparison.Ordinal);
    }

    private static string NormalizeComparableVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "0000000000";
        }

        var parts = new List<string>();
        var buffer = new List<char>();

        foreach (var character in value)
        {
            if (char.IsDigit(character))
            {
                buffer.Add(character);
                continue;
            }

            if (buffer.Count > 0)
            {
                parts.Add(new string(buffer.ToArray()).PadLeft(10, '0'));
                buffer.Clear();
            }
        }

        if (buffer.Count > 0)
        {
            parts.Add(new string(buffer.ToArray()).PadLeft(10, '0'));
        }

        if (parts.Count == 0)
        {
            return value;
        }

        return string.Join(".", parts);
    }
    private static bool Is(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasFlag(string[] args, string flag)
    {
        return args.Any(x => string.Equals(x, flag, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetRequiredOption(string[] args, string option)
    {
        return GetOptionalOption(args, option);
    }

    private static string? GetOptionalOption(string[] args, string option)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static List<string> GetMultiOption(string[] args, string option)
    {
        var values = new List<string>();

        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
            {
                values.Add(args[index + 1]);
            }
        }

        return values;
    }

    private static string? NormalizeVanillaType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "release" => "release",
            "snapshot" => "snapshot",
            "beta" => "beta",
            "alpha" => "alpha",
            "experimental" => "experimental",
            _ => null
        };
    }

    private static bool TryParseCatalogProvider(string? value, out CatalogProvider provider)
    {
        provider = CatalogProvider.Modrinth;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "modrinth" => true,
            "curseforge" => (provider = CatalogProvider.CurseForge) == CatalogProvider.CurseForge,
            _ => false
        };
    }

    private static bool TryParseCatalogSearchSort(string? value, out CatalogSearchSort sort)
    {
        sort = CatalogSearchSort.Relevance;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "relevance":
                sort = CatalogSearchSort.Relevance;
                return true;
            case "downloads":
                sort = CatalogSearchSort.Downloads;
                return true;
            case "follows":
                sort = CatalogSearchSort.Follows;
                return true;
            case "newest":
                sort = CatalogSearchSort.Newest;
                return true;
            case "updated":
                sort = CatalogSearchSort.Updated;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseInt32(string? value, int defaultValue, int minValue, int maxValue, out int result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = defaultValue;
            return true;
        }

        if (int.TryParse(value, out result) && result >= minValue && result <= maxValue)
        {
            return true;
        }

        result = defaultValue;
        return false;
    }

    private static bool MatchesVanillaType(dynamic version, string[] requestedTypes)
    {
        var versionId = (string?)version.VersionId?.ToString() ?? string.Empty;
        var isRelease = (bool)version.IsRelease;

        foreach (var requestedType in requestedTypes)
        {
            switch (requestedType)
            {
                case "release":
                    if (isRelease)
                    {
                        return true;
                    }
                    break;

                case "snapshot":
                    if (!isRelease &&
                        !versionId.Contains("alpha", StringComparison.OrdinalIgnoreCase) &&
                        !versionId.Contains("beta", StringComparison.OrdinalIgnoreCase) &&
                        !versionId.Contains("experimental", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    break;

                case "beta":
                    if (versionId.Contains("beta", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    break;

                case "alpha":
                    if (versionId.Contains("alpha", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    break;

                case "experimental":
                    if (versionId.Contains("experimental", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    break;
            }
        }

        return false;
    }

    private static void WriteHelp(bool outputJson)
    {
        var payload = new
        {
            Commands = new[]
            {
                "accounts list [--json]",
                "accounts add-offline --username <name> [--set-default] [--json]",
                "accounts set-default --account-id <id> [--json]",
                "accounts remove --account-id <id> [--json]",
                "instances install --name <name> --version <version> --loader <vanilla|fabric|quilt|forge|neoforge> [--loader-version <version>] [--path <directory>] [--overwrite] [--download-runtime] [--json]",
                "instances verify --instance-id <id> [--json]",
                "instances repair --instance-id <id> [--json]",                
                "instances start --instance-id <id> | --name <instance-name> [--account-id <id>] [--json]",
                "launch plan --instance-id <id> --java <path> [--main-class <class>] [--classpath <entry> ...] [--assets-dir <path>] [--asset-index <id>] [--account-id <id>] [--json]",
                "launch run --instance-id <id> --java <path> [--main-class <class>] [--classpath <entry> ...] [--assets-dir <path>] [--asset-index <id>] [--account-id <id>] [--json]",
                "launch status --launch-id <guid> [--json]",
                "launch stop --launch-id <guid> [--json]",
                "catalog mods [--provider <modrinth|curseforge>] [--loader <fabric|quilt|forge|neoforge>] [--game-version <version>] [--query <text>] [--category <value> ...] [--sort <relevance|downloads|follows|newest|updated>] [--limit <1-100>] [--offset <0+>] [--json]",
                "catalog modpacks [--provider <modrinth|curseforge>] [--game-version <version>] [--query <text>] [--category <value> ...] [--sort <relevance|downloads|follows|newest|updated>] [--limit <1-100>] [--offset <0+>] [--json]",
                "catalog resourcepacks [--provider <modrinth|curseforge>] [--game-version <version>] [--query <text>] [--category <value> ...] [--sort <relevance|downloads|follows|newest|updated>] [--limit <1-100>] [--offset <0+>] [--json]",
                "catalog shaders [--provider <modrinth|curseforge>] [--game-version <version>] [--query <text>] [--category <value> ...] [--sort <relevance|downloads|follows|newest|updated>] [--limit <1-100>] [--offset <0+>] [--json]",
                "versions vanilla [--type <release|snapshot|beta|alpha|experimental>] [--latest] [--json]",
                "versions loaders --loader <fabric|quilt|forge|neoforge> --game-version <version> [--json]",
                "diagnostics dump [--output <path>] [--json]",
                "instance content list --instance-id <id> [--json]",
                "instance content rescan --instance-id <id> [--json]",
                "instance mods disable --instance-id <id> --mod <name-or-relative-path> [--json]",
                "instance mods enable --instance-id <id> --mod <name-or-relative-path> [--json]"
            }
        };

        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
            return;
        }

        Console.WriteLine("BlockiumLauncher CLI");
        foreach (var command in payload.Commands)
        {
            Console.WriteLine("  " + command);
        }
    }

    private static void WriteSuccess<T>(T payload, bool outputJson, Action<List<string>> buildLines)
    {
        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Success = true,
                Data = payload
            }, JsonOptions));
            return;
        }

        var lines = new List<string>();
        buildLines(lines);
        foreach (var line in lines)
        {
            Console.WriteLine(line);
        }
    }

    private static void WriteFailure(string code, string message, bool outputJson)
    {
        if (outputJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Success = false,
                Error = new
                {
                    Code = code,
                    Message = message
                }
            }, JsonOptions));
            return;
        }

        Console.Error.WriteLine(code + ": " + message);
    }

    private static int WriteInstanceContentMetadata(InstanceContentMetadata metadata, bool outputJson)
    {
        WriteSuccess(metadata, outputJson, lines =>
        {
            lines.Add($"IndexedAtUtc: {metadata.IndexedAtUtc:O}");
            lines.Add($"IconPath: {metadata.IconPath ?? "(none)"}");
            lines.Add($"TotalPlaytimeSeconds: {metadata.TotalPlaytimeSeconds}");
            lines.Add($"LastLaunchAtUtc: {(metadata.LastLaunchAtUtc is null ? "(never)" : $"{metadata.LastLaunchAtUtc.Value:O}")}");
            lines.Add($"LastLaunchPlaytimeSeconds: {(metadata.LastLaunchPlaytimeSeconds is null ? "(none)" : metadata.LastLaunchPlaytimeSeconds.ToString())}");
            lines.Add($"Mods: {metadata.Mods.Count}");
            lines.Add($"ResourcePacks: {metadata.ResourcePacks.Count}");
            lines.Add($"Shaders: {metadata.Shaders.Count}");
            lines.Add($"Worlds: {metadata.Worlds.Count}");
            lines.Add($"Screenshots: {metadata.Screenshots.Count}");
            lines.Add($"Servers: {metadata.Servers.Count}");

            foreach (var mod in metadata.Mods)
            {
                lines.Add($"mod | {mod.Name} | Disabled={mod.IsDisabled} | {mod.RelativePath}");
            }
        });

        return CliExitCodes.Success;
    }
}
