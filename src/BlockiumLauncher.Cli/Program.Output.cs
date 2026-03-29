using BlockiumLauncher.Application.Abstractions.Instances;
using System.Text.Json;

namespace BlockiumLauncher.Cli;

internal static partial class Program
{
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
                "catalog files --provider <curseforge> --type <mod|modpack|resourcepack|shader> --project-id <id> [--game-version <version>] [--loader <fabric|quilt|forge|neoforge>] [--limit <1-50>] [--offset <0+>] [--json]",
                "versions vanilla [--type <release|snapshot|beta|alpha|experimental>] [--latest] [--json]",
                "versions loaders --loader <fabric|quilt|forge|neoforge> --game-version <version> [--json]",
                "diagnostics dump [--output <path>] [--json]",
                "instance content list --instance-id <id> [--json]",
                "instance content rescan --instance-id <id> [--json]",
                "instance content install --provider <curseforge> --type <mod|resourcepack|shader> --instance-id <id> --project-id <id> [--file-id <id>] [--game-version <version>] [--loader <fabric|quilt|forge|neoforge>] [--overwrite] [--json]",
                "instance mods disable --instance-id <id> --mod <name-or-relative-path> [--json]",
                "instance mods enable --instance-id <id> --mod <name-or-relative-path> [--json]",
                "instances import-modpack --provider <curseforge> --project-id <id> --name <instance-name> [--file-id <id>] [--path <directory>] [--overwrite] [--download-runtime] [--downloads-path <directory>] [--wait-for-manual-downloads] [--wait-timeout-seconds <seconds>] [--open-manual-downloads] [--json]",
                "instances resume-modpack-import --instance-id <id> | --name <instance-name> [--downloads-path <directory>] [--wait-for-manual-downloads] [--wait-timeout-seconds <seconds>] [--open-manual-downloads] [--json]"
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
