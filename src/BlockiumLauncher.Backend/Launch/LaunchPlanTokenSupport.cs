using System.Text;
using System.Text.RegularExpressions;
using BlockiumLauncher.Application.Abstractions.Launch;
using BlockiumLauncher.Contracts.Accounts;
using BlockiumLauncher.Domain.Entities;

namespace BlockiumLauncher.Application.UseCases.Launch;

internal static class LaunchPlanTokenSupport
{
    internal static Dictionary<string, string> BuildTokenMap(
        LauncherInstance Instance,
        LaunchAccountContextDto Account,
        string WorkingDirectory,
        string? AssetsDirectory,
        string? AssetIndexId,
        string? NativesDirectory,
        string ResolvedMainClass,
        IReadOnlyList<string> ClasspathEntries,
        string ClasspathText,
        RuntimeMetadata? RuntimeMetadata)
    {
        var GameVersion = Instance.GameVersion.ToString();
        var LoaderVersion = Instance.LoaderVersion?.ToString() ?? string.Empty;
        var VersionName = !string.IsNullOrWhiteSpace(RuntimeMetadata?.Version)
            ? RuntimeMetadata.Version
            : (!string.IsNullOrWhiteSpace(LoaderVersion) ? LoaderVersion : GameVersion);

        var LibraryDirectory = LaunchPlanRuntimeSupport.TryResolveLibraryDirectory(ClasspathEntries, RuntimeMetadata);

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["${auth_player_name}"] = Account.Username ?? string.Empty,
            ["${version_name}"] = VersionName,
            ["${game_directory}"] = WorkingDirectory,
            ["${game_assets}"] = AssetsDirectory ?? string.Empty,
            ["${assets_root}"] = AssetsDirectory ?? string.Empty,
            ["${assets_index_name}"] = AssetIndexId ?? string.Empty,
            ["${auth_uuid}"] = Account.PlayerUuid ?? string.Empty,
            ["${auth_access_token}"] = "0",
            ["${auth_session}"] = "0",
            ["${user_type}"] = "legacy",
            ["${version_type}"] = "release",
            ["${natives_directory}"] = NativesDirectory ?? string.Empty,
            ["${launcher_name}"] = "BlockiumLauncher",
            ["${launcher_version}"] = "1.0.0",
            ["${classpath}"] = ClasspathText,
            ["${classpath_separator}"] = Path.PathSeparator.ToString(),
            ["${library_directory}"] = LibraryDirectory,
            ["${user_properties}"] = string.IsNullOrWhiteSpace(Account.UserPropertiesJson) ? "{}" : Account.UserPropertiesJson,
            ["${clientid}"] = string.Empty,
            ["${auth_xuid}"] = string.Empty,
            ["${resolution_width}"] = string.Empty,
            ["${resolution_height}"] = string.Empty,
            ["${main_class}"] = ResolvedMainClass
        };
    }

    internal static IEnumerable<string> ResolveRuntimeArguments(
        IEnumerable<string>? SourceArguments,
        IReadOnlyDictionary<string, string> TokenMap)
    {
        if (SourceArguments is null)
        {
            yield break;
        }

        foreach (var Arg in SourceArguments)
        {
            if (string.IsNullOrWhiteSpace(Arg))
            {
                continue;
            }

            var Expanded = NormalizeJvmArgument(ExpandTokens(Arg, TokenMap)).Trim();
            if (Expanded.Length == 0)
            {
                continue;
            }

            foreach (var Token in SplitCommandLineArguments(Expanded))
            {
                if (!string.IsNullOrWhiteSpace(Token))
                {
                    yield return Token;
                }
            }
        }
    }

    internal static string ExpandTokens(string Value, IReadOnlyDictionary<string, string> TokenMap)
    {
        var Result = Value;

        foreach (var Pair in TokenMap)
        {
            Result = Result.Replace(Pair.Key, Pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return Result;
    }

    private static string NormalizeJvmArgument(string Value)
    {
        if (string.IsNullOrWhiteSpace(value: Value))
        {
            return Value;
        }

        return Regex.Replace(
            Value,
            @"^(-D[^=\s]+)=\s+(.+?)\s*$",
            "$1=$2");
    }

    private static IEnumerable<string> SplitCommandLineArguments(string Value)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            yield break;
        }

        var Buffer = new StringBuilder();
        var InQuotes = false;

        for (var Index = 0; Index < Value.Length; Index++)
        {
            var Character = Value[Index];

            if (Character == '"')
            {
                InQuotes = !InQuotes;
                continue;
            }

            if (char.IsWhiteSpace(Character) && !InQuotes)
            {
                if (Buffer.Length > 0)
                {
                    yield return Buffer.ToString();
                    Buffer.Clear();
                }

                continue;
            }

            Buffer.Append(Character);
        }

        if (Buffer.Length > 0)
        {
            yield return Buffer.ToString();
        }
    }
}
