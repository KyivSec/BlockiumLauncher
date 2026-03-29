using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Launch;
using BlockiumLauncher.Contracts.Launch;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;
using Microsoft.Extensions.DependencyInjection;

namespace BlockiumLauncher.Cli;

internal static partial class Program
{
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
}
