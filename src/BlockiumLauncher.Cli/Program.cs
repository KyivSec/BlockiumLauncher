using System.Text.Json;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Backend.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace BlockiumLauncher.Cli;

internal static partial class Program
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
            ["catalog files"] = static (serviceProvider, args, outputJson) => HandleCatalogFilesAsync(serviceProvider, args, outputJson),
            ["catalog key set"] = static (serviceProvider, args, outputJson) => HandleCatalogKeySetAsync(serviceProvider, args, outputJson),
            ["catalog key clear"] = static (serviceProvider, args, outputJson) => HandleCatalogKeyClearAsync(serviceProvider, outputJson),
            ["catalog key status"] = static (serviceProvider, args, outputJson) => HandleCatalogKeyStatusAsync(serviceProvider, outputJson),
            ["versions vanilla"] = static (serviceProvider, args, outputJson) => HandleVersionsVanillaAsync(serviceProvider, args, outputJson),
            ["versions loaders"] = static (serviceProvider, args, outputJson) => HandleVersionsLoadersAsync(serviceProvider, args, outputJson),
            ["diagnostics dump"] = static (serviceProvider, args, outputJson) => HandleDiagnosticsDumpAsync(serviceProvider, args, outputJson),
            ["instance content list"] = static (serviceProvider, args, outputJson) => HandleInstanceContentListAsync(serviceProvider, args, outputJson),
            ["instance content rescan"] = static (serviceProvider, args, outputJson) => HandleInstanceContentRescanAsync(serviceProvider, args, outputJson),
            ["instance content install"] = static (serviceProvider, args, outputJson) => HandleInstanceContentInstallAsync(serviceProvider, args, outputJson),
            ["instance mods disable"] = static (serviceProvider, args, outputJson) => HandleInstanceModEnabledAsync(serviceProvider, args, enabled: false, outputJson),
            ["instance mods enable"] = static (serviceProvider, args, outputJson) => HandleInstanceModEnabledAsync(serviceProvider, args, enabled: true, outputJson),
            ["instances import-modpack"] = static (serviceProvider, args, outputJson) => HandleCatalogImportModpackAsync(serviceProvider, args, outputJson),
            ["instances resume-modpack-import"] = static (serviceProvider, args, outputJson) => HandleCatalogResumeModpackImportAsync(serviceProvider, args, outputJson)
        };

    public static async Task<int> Main(string[] args)
    {
        var outputJson = args.Any(x => string.Equals(x, "--json", StringComparison.OrdinalIgnoreCase));

        try
        {
            var filteredArgs = args.Where(x => !string.Equals(x, "--json", StringComparison.OrdinalIgnoreCase)).ToArray();

            var services = new ServiceCollection();
            services.AddBlockiumLauncherBackend();

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
}
