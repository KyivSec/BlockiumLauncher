using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Catalog;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace BlockiumLauncher.Cli;

internal static partial class Program
{
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

    private static async Task<int> HandleCatalogFilesAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var providerText = GetOptionalOption(args, "--provider") ?? "curseforge";
        var typeText = GetRequiredOption(args, "--type");
        var projectId = GetRequiredOption(args, "--project-id");
        var loader = GetOptionalOption(args, "--loader");
        var gameVersion = GetOptionalOption(args, "--game-version");

        if (!TryParseCatalogProvider(providerText, out var provider) ||
            !TryParseCatalogContentType(typeText, out var contentType) ||
            string.IsNullOrWhiteSpace(projectId))
        {
            WriteFailure(
                "Cli.InvalidArguments",
                "Required: --provider <curseforge> --type <mod|modpack|resourcepack|shader> --project-id <id> [--game-version <version>] [--loader <fabric|quilt|forge|neoforge>] [--limit <1-50>] [--offset <0+>]",
                outputJson);
            return CliExitCodes.InvalidArguments;
        }

        if (!TryParseInt32(GetOptionalOption(args, "--limit"), 20, 1, 50, out var limit) ||
            !TryParseInt32(GetOptionalOption(args, "--offset"), 0, 0, int.MaxValue, out var offset))
        {
            WriteFailure("Cli.InvalidArguments", "Invalid paging options. --limit must be 1-50 and --offset must be 0 or greater.", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        var useCase = serviceProvider.GetRequiredService<ListCatalogFilesUseCase>();
        var result = await useCase.ExecuteAsync(new ListCatalogFilesRequest
        {
            Provider = provider,
            ContentType = contentType,
            ProjectId = projectId,
            GameVersion = gameVersion,
            Loader = loader,
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
                lines.Add("No matching files found.");
                return;
            }

            foreach (var item in result.Value)
            {
                lines.Add($"{item.DisplayName} | FileId={item.FileId} | Name={item.FileName} | Loaders={string.Join(",", item.Loaders)} | Versions={string.Join(",", item.GameVersions)}");
            }
        });

        return CliExitCodes.Success;
    }

    private static async Task<int> HandleCatalogImportModpackAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var providerText = GetOptionalOption(args, "--provider") ?? "curseforge";
        var projectId = GetRequiredOption(args, "--project-id");
        var fileId = GetOptionalOption(args, "--file-id");
        var instanceName = GetRequiredOption(args, "--name");
        var targetDirectory = GetOptionalOption(args, "--path");
        var overwrite = HasFlag(args, "--overwrite");
        var downloadRuntime = HasFlag(args, "--download-runtime");
        var downloadsPath = GetOptionalOption(args, "--downloads-path");
        var waitForManualDownloads = HasFlag(args, "--wait-for-manual-downloads");
        var openManualDownloads = HasFlag(args, "--open-manual-downloads");

        if (!TryParseCatalogProvider(providerText, out var provider) ||
            string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(instanceName) ||
            !TryParseWaitTimeout(args, out var waitTimeout))
        {
            WriteFailure(
                "Cli.InvalidArguments",
                "Required: --provider <curseforge> --project-id <id> --name <instance-name> [--file-id <id>] [--path <directory>] [--overwrite] [--download-runtime] [--downloads-path <directory>] [--wait-for-manual-downloads] [--wait-timeout-seconds <1-86400>] [--open-manual-downloads]",
                outputJson);
            return CliExitCodes.InvalidArguments;
        }

        var useCase = serviceProvider.GetRequiredService<ImportCatalogModpackUseCase>();
        var result = await useCase.ExecuteAsync(new ImportCatalogModpackRequest
        {
            Provider = provider,
            ProjectId = projectId,
            FileId = fileId,
            InstanceName = instanceName,
            TargetDirectory = targetDirectory,
            OverwriteIfExists = overwrite,
            DownloadRuntime = downloadRuntime,
            DownloadsDirectory = downloadsPath,
            WaitForManualDownloads = waitForManualDownloads,
            WaitTimeout = waitTimeout
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
            result.Value.File.ProjectId,
            result.Value.File.FileId,
            result.Value.InstalledPath,
            result.Value.DownloadsDirectory,
            result.Value.IsCompleted,
            PendingManualDownloads = result.Value.PendingManualDownloads.Select(file => new
            {
                file.ProjectId,
                file.FileId,
                file.DisplayName,
                file.FileName,
                file.DestinationRelativePath,
                file.FilePageUrl,
                file.ProjectUrl,
                file.SizeBytes
            }).ToArray()
        };

        if (openManualDownloads && result.Value.PendingManualDownloads.Count > 0)
        {
            OpenUrisInDefaultBrowser(result.Value.PendingManualDownloads.Select(static file => file.FilePageUrl ?? file.ProjectUrl));
        }

        WriteSuccess(payload, outputJson, lines =>
        {
            lines.Add($"Modpack imported: {payload.Name} ({payload.InstanceId})");
            lines.Add($"GameVersion: {payload.GameVersion}");
            lines.Add($"Loader: {payload.LoaderType}");
            lines.Add($"LoaderVersion: {payload.LoaderVersion ?? ""}");
            lines.Add($"ProjectId: {payload.ProjectId}");
            lines.Add($"FileId: {payload.FileId}");
            lines.Add($"InstalledPath: {payload.InstalledPath}");
            lines.Add($"DownloadsDirectory: {payload.DownloadsDirectory}");

            if (!payload.IsCompleted)
            {
                lines.Add($"Manual downloads required: {payload.PendingManualDownloads.Length}");
                lines.Add("Open the listed CurseForge file pages, download the files to the Downloads directory, then run `instances resume-modpack-import`.");

                foreach (var file in payload.PendingManualDownloads)
                {
                    lines.Add($"{file.DisplayName} | FileId={file.FileId} | {file.FileName} | {file.FilePageUrl ?? file.ProjectUrl ?? "(no link)"}");
                }
            }
            else
            {
                lines.Add("Modpack content import completed.");
            }
        });

        return CliExitCodes.Success;
    }

    private static async Task<int> HandleCatalogResumeModpackImportAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var instanceId = GetOptionalOption(args, "--instance-id");
        var instanceName = GetOptionalOption(args, "--name");
        var downloadsPath = GetOptionalOption(args, "--downloads-path");
        var waitForManualDownloads = HasFlag(args, "--wait-for-manual-downloads");
        var openManualDownloads = HasFlag(args, "--open-manual-downloads");

        if ((string.IsNullOrWhiteSpace(instanceId) && string.IsNullOrWhiteSpace(instanceName)) ||
            !TryParseWaitTimeout(args, out var waitTimeout))
        {
            WriteFailure(
                "Cli.InvalidArguments",
                "Required: --instance-id <id> or --name <instance-name> [--downloads-path <directory>] [--wait-for-manual-downloads] [--wait-timeout-seconds <1-86400>] [--open-manual-downloads]",
                outputJson);
            return CliExitCodes.InvalidArguments;
        }

        var useCase = serviceProvider.GetRequiredService<ResumeCatalogModpackImportUseCase>();
        var result = await useCase.ExecuteAsync(new ResumeCatalogModpackImportRequest
        {
            InstanceId = instanceId,
            InstanceName = instanceName,
            DownloadsDirectory = downloadsPath,
            WaitForManualDownloads = waitForManualDownloads,
            WaitTimeout = waitTimeout
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        if (openManualDownloads && result.Value.PendingManualDownloads.Count > 0)
        {
            OpenUrisInDefaultBrowser(result.Value.PendingManualDownloads.Select(static file => file.FilePageUrl ?? file.ProjectUrl));
        }

        var payload = new
        {
            InstanceId = result.Value.Instance.InstanceId.ToString(),
            result.Value.Instance.Name,
            result.Value.DownloadsDirectory,
            result.Value.ImportedFiles,
            result.Value.IsCompleted,
            PendingManualDownloads = result.Value.PendingManualDownloads.Select(file => new
            {
                file.ProjectId,
                file.FileId,
                file.DisplayName,
                file.FileName,
                file.DestinationRelativePath,
                file.FilePageUrl,
                file.ProjectUrl,
                file.SizeBytes
            }).ToArray()
        };

        WriteSuccess(payload, outputJson, lines =>
        {
            lines.Add($"Instance: {payload.Name} ({payload.InstanceId})");
            lines.Add($"DownloadsDirectory: {payload.DownloadsDirectory}");
            lines.Add($"ImportedFiles: {payload.ImportedFiles.Count}");

            foreach (var importedFile in payload.ImportedFiles)
            {
                lines.Add(importedFile);
            }

            if (!payload.IsCompleted)
            {
                lines.Add($"Manual downloads still pending: {payload.PendingManualDownloads.Length}");
                foreach (var file in payload.PendingManualDownloads)
                {
                    lines.Add($"{file.DisplayName} | FileId={file.FileId} | {file.FileName} | {file.FilePageUrl ?? file.ProjectUrl ?? "(no link)"}");
                }
            }
            else
            {
                lines.Add("Modpack import completed.");
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
}
