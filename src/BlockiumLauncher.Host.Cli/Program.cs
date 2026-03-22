using System.Text.Json;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Accounts;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Application.UseCases.Launch;
using BlockiumLauncher.Contracts.Launch;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace BlockiumLauncher.Host.Cli;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static async Task<int> Main(string[] args)
    {
        var outputJson = args.Any(x => string.Equals(x, "--json", StringComparison.OrdinalIgnoreCase));

        try
        {
            var filteredArgs = args.Where(x => !string.Equals(x, "--json", StringComparison.OrdinalIgnoreCase)).ToArray();

            var services = new ServiceCollection();
            services.AddBlockiumLauncherInfrastructure();

            using var serviceProvider = services.BuildServiceProvider();

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
        if (args.Length >= 2 && Is(args[0], "accounts") && Is(args[1], "list"))
        {
            return await HandleAccountsListAsync(serviceProvider, outputJson).ConfigureAwait(false);
        }

        if (args.Length >= 2 && Is(args[0], "accounts") && Is(args[1], "add-offline"))
        {
            return await HandleAccountsAddOfflineAsync(serviceProvider, args.Skip(2).ToArray(), outputJson).ConfigureAwait(false);
        }

        if (args.Length >= 2 && Is(args[0], "accounts") && Is(args[1], "set-default"))
        {
            return await HandleAccountsSetDefaultAsync(serviceProvider, args.Skip(2).ToArray(), outputJson).ConfigureAwait(false);
        }

        if (args.Length >= 2 && Is(args[0], "accounts") && Is(args[1], "remove"))
        {
            return await HandleAccountsRemoveAsync(serviceProvider, args.Skip(2).ToArray(), outputJson).ConfigureAwait(false);
        }

        if (args.Length >= 2 && Is(args[0], "instances") && Is(args[1], "verify"))
        {
            return await HandleInstancesVerifyAsync(serviceProvider, args.Skip(2).ToArray(), outputJson).ConfigureAwait(false);
        }

        if (args.Length >= 2 && Is(args[0], "instances") && Is(args[1], "repair"))
        {
            return await HandleInstancesRepairAsync(serviceProvider, args.Skip(2).ToArray(), outputJson).ConfigureAwait(false);
        }

        if (args.Length >= 2 && Is(args[0], "launch") && Is(args[1], "plan"))
        {
            return await HandleLaunchPlanAsync(serviceProvider, args.Skip(2).ToArray(), outputJson).ConfigureAwait(false);
        }

        if (args.Length >= 2 && Is(args[0], "launch") && Is(args[1], "run"))
        {
            return await HandleLaunchRunAsync(serviceProvider, args.Skip(2).ToArray(), outputJson).ConfigureAwait(false);
        }

        if (args.Length >= 2 && Is(args[0], "launch") && Is(args[1], "status"))
        {
            return await HandleLaunchStatusAsync(serviceProvider, args.Skip(2).ToArray(), outputJson).ConfigureAwait(false);
        }

        if (args.Length >= 2 && Is(args[0], "launch") && Is(args[1], "stop"))
        {
            return await HandleLaunchStopAsync(serviceProvider, args.Skip(2).ToArray(), outputJson).ConfigureAwait(false);
        }

        if (args.Length >= 2 && Is(args[0], "versions") && Is(args[1], "vanilla"))
        {
            return await HandleVersionsVanillaAsync(serviceProvider, outputJson).ConfigureAwait(false);
        }

        if (args.Length >= 2 && Is(args[0], "versions") && Is(args[1], "loaders"))
        {
            return await HandleVersionsLoadersAsync(serviceProvider, args.Skip(2).ToArray(), outputJson).ConfigureAwait(false);
        }

        WriteHelp(outputJson);
        return CliExitCodes.InvalidArguments;
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
        var result = await useCase.ExecuteAsync(new RepairInstanceRequest
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
        var mainClass = GetRequiredOption(args, "--main-class");
        var accountIdText = GetOptionalOption(args, "--account-id");
        var assetsDir = GetOptionalOption(args, "--assets-dir");
        var assetIndex = GetOptionalOption(args, "--asset-index");
        var classpathEntries = GetMultiOption(args, "--classpath");

        if (string.IsNullOrWhiteSpace(instanceIdText) ||
            string.IsNullOrWhiteSpace(javaPath) ||
            string.IsNullOrWhiteSpace(mainClass) ||
            classpathEntries.Count == 0)
        {
            WriteFailure(
                "Cli.InvalidArguments",
                "Required: --instance-id <id> --java <path> --main-class <class> --classpath <entry> [--classpath <entry> ...]",
                outputJson);
            return CliExitCodes.InvalidArguments;
        }

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

    private static async Task<int> HandleVersionsVanillaAsync(IServiceProvider serviceProvider, bool outputJson)
    {
        var service = serviceProvider.GetRequiredService<IVersionManifestService>();
        var result = await service.GetAvailableVersionsAsync(CancellationToken.None).ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        var payload = result.Value.ToArray();

        WriteSuccess(payload, outputJson, lines =>
        {
            if (payload.Length == 0)
            {
                lines.Add("No vanilla versions returned.");
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
        var gameVersionText = GetRequiredOption(args, "--game-version");

        if (string.IsNullOrWhiteSpace(loaderText) || string.IsNullOrWhiteSpace(gameVersionText))
        {
            WriteFailure("Cli.InvalidArguments", "Required: --loader <fabric|quilt|forge|neoforge|vanilla> --game-version <version>", outputJson);
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

        var payload = result.Value.ToArray();

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

    private static async Task<(bool IsFailure, int ExitCode, string? ErrorCode, string? ErrorMessage, LaunchPlanDto? Plan)> BuildLaunchPlanAsync(IServiceProvider serviceProvider, string[] args)
    {
        var instanceIdText = GetRequiredOption(args, "--instance-id");
        var javaPath = GetRequiredOption(args, "--java");
        var mainClass = GetRequiredOption(args, "--main-class");
        var accountIdText = GetOptionalOption(args, "--account-id");
        var assetsDir = GetOptionalOption(args, "--assets-dir");
        var assetIndex = GetOptionalOption(args, "--asset-index");
        var classpathEntries = GetMultiOption(args, "--classpath");

        if (string.IsNullOrWhiteSpace(instanceIdText) ||
            string.IsNullOrWhiteSpace(javaPath) ||
            string.IsNullOrWhiteSpace(mainClass) ||
            classpathEntries.Count == 0)
        {
            return (true, CliExitCodes.InvalidArguments, "Cli.InvalidArguments", "Required: --instance-id <id> --java <path> --main-class <class> --classpath <entry> [--classpath <entry> ...]", null);
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
                "instances verify --instance-id <id> [--json]",
                "instances repair --instance-id <id> [--json]",
                "launch plan --instance-id <id> --java <path> --main-class <class> --classpath <entry> [--classpath <entry> ...] [--assets-dir <path>] [--asset-index <id>] [--account-id <id>] [--json]",
                "launch run --instance-id <id> --java <path> --main-class <class> --classpath <entry> [--classpath <entry> ...] [--assets-dir <path>] [--asset-index <id>] [--account-id <id>] [--json]",
                "launch status --launch-id <guid> [--json]",
                "launch stop --launch-id <guid> [--json]",
                "versions vanilla [--json]",
                "versions loaders --loader <fabric|quilt|forge|neoforge> --game-version <version> [--json]"
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
}