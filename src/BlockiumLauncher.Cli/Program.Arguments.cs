using BlockiumLauncher.Application.UseCases.Common;
using System.Diagnostics;

namespace BlockiumLauncher.Cli;

internal static partial class Program
{
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

    private static bool TryParseCatalogContentType(string? value, out CatalogContentType contentType)
    {
        contentType = CatalogContentType.Mod;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "mod":
            case "mods":
                contentType = CatalogContentType.Mod;
                return true;
            case "modpack":
            case "modpacks":
                contentType = CatalogContentType.Modpack;
                return true;
            case "resourcepack":
            case "resourcepacks":
            case "resource-pack":
            case "resource-packs":
                contentType = CatalogContentType.ResourcePack;
                return true;
            case "shader":
            case "shaders":
            case "shaderpack":
            case "shaderpacks":
                contentType = CatalogContentType.Shader;
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

    private static bool TryParseWaitTimeout(string[] args, out TimeSpan timeout)
    {
        if (!TryParseInt32(GetOptionalOption(args, "--wait-timeout-seconds"), 1800, 1, 86400, out var seconds))
        {
            timeout = TimeSpan.FromMinutes(30);
            return false;
        }

        timeout = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static void OpenUrisInDefaultBrowser(IEnumerable<string?> uris)
    {
        foreach (var uri in uris
            .Where(static uri => !string.IsNullOrWhiteSpace(uri))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri!,
                UseShellExecute = true
            });
        }
    }
}
