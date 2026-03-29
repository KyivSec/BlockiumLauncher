using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.Abstractions.Paths;
using BlockiumLauncher.Application.UseCases.Catalog;
using BlockiumLauncher.Application.UseCases.Instances;
using BlockiumLauncher.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace BlockiumLauncher.Cli;

internal static partial class Program
{
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

    private static async Task<int> HandleInstanceContentInstallAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var providerText = GetOptionalOption(args, "--provider") ?? "curseforge";
        var typeText = GetRequiredOption(args, "--type");
        var instanceIdText = GetRequiredOption(args, "--instance-id");
        var projectId = GetRequiredOption(args, "--project-id");
        var fileId = GetOptionalOption(args, "--file-id");
        var loader = GetOptionalOption(args, "--loader");
        var gameVersion = GetOptionalOption(args, "--game-version");
        var overwrite = HasFlag(args, "--overwrite");

        if (!TryParseCatalogProvider(providerText, out var provider) ||
            !TryParseCatalogContentType(typeText, out var contentType) ||
            string.IsNullOrWhiteSpace(instanceIdText) ||
            string.IsNullOrWhiteSpace(projectId))
        {
            WriteFailure(
                "Cli.InvalidArguments",
                "Required: --provider <curseforge> --type <mod|resourcepack|shader> --instance-id <id> --project-id <id> [--file-id <id>] [--game-version <version>] [--loader <fabric|quilt|forge|neoforge>] [--overwrite]",
                outputJson);
            return CliExitCodes.InvalidArguments;
        }

        var useCase = serviceProvider.GetRequiredService<InstallCatalogContentUseCase>();
        var result = await useCase.ExecuteAsync(new InstallCatalogContentRequest
        {
            Provider = provider,
            ContentType = contentType,
            InstanceId = new InstanceId(instanceIdText),
            ProjectId = projectId,
            FileId = fileId,
            GameVersion = gameVersion,
            Loader = loader,
            OverwriteExisting = overwrite
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
            result.Value.File.ProjectId,
            result.Value.File.FileId,
            result.Value.File.DisplayName,
            result.Value.File.FileName,
            result.Value.InstalledPath
        };

        WriteSuccess(payload, outputJson, lines =>
        {
            lines.Add($"Content installed into {payload.Name} ({payload.InstanceId})");
            lines.Add($"ProjectId: {payload.ProjectId}");
            lines.Add($"FileId: {payload.FileId}");
            lines.Add($"DisplayName: {payload.DisplayName}");
            lines.Add($"FileName: {payload.FileName}");
            lines.Add($"InstalledPath: {payload.InstalledPath}");
        });

        return CliExitCodes.Success;
    }
}
