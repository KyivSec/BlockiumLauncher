using BlockiumLauncher.Contracts.Accounts;
using BlockiumLauncher.Contracts.Launch;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;

namespace BlockiumLauncher.Application.UseCases.Launch;

internal static class LaunchPlanArgumentBuilder
{
    internal static List<LaunchArgumentDto> BuildJvmArguments(
        LauncherInstance Instance,
        string? NativesDirectory,
        string ClasspathText,
        IReadOnlyDictionary<string, string> TokenMap,
        IEnumerable<string>? RuntimeArguments)
    {
        var JvmArguments = new List<LaunchArgumentDto>
        {
            new() { Value = "-Xms" + Instance.LaunchProfile.MinMemoryMb + "m" },
            new() { Value = "-Xmx" + Instance.LaunchProfile.MaxMemoryMb + "m" }
        };

        if (!string.IsNullOrWhiteSpace(NativesDirectory))
        {
            JvmArguments.Add(new LaunchArgumentDto
            {
                Value = "-Djava.library.path=" + NativesDirectory
            });
        }

        foreach (var Arg in LaunchPlanTokenSupport.ResolveRuntimeArguments(RuntimeArguments, TokenMap))
        {
            JvmArguments.Add(new LaunchArgumentDto { Value = Arg });
        }

        JvmArguments.Add(new LaunchArgumentDto { Value = "-cp" });
        JvmArguments.Add(new LaunchArgumentDto { Value = ClasspathText });

        foreach (var Arg in Instance.LaunchProfile.ExtraJvmArgs)
        {
            if (!string.IsNullOrWhiteSpace(Arg))
            {
                JvmArguments.Add(new LaunchArgumentDto
                {
                    Value = LaunchPlanTokenSupport.ExpandTokens(Arg, TokenMap)
                });
            }
        }

        return JvmArguments;
    }

    internal static List<LaunchArgumentDto> BuildGameArguments(
        LauncherInstance Instance,
        string WorkingDirectory,
        string? AssetsDirectory,
        string? AssetIndexId,
        LaunchAccountContextDto Account,
        IReadOnlyDictionary<string, string> TokenMap,
        IEnumerable<string>? RuntimeArguments)
    {
        var GameArguments = new List<LaunchArgumentDto>
        {
            new() { Value = "--username" },
            new() { Value = Account.Username },
            new() { Value = "--version" },
            new() { Value = Instance.GameVersion.ToString() },
            new() { Value = "--gameDir" },
            new() { Value = WorkingDirectory },
            new() { Value = "--uuid" },
            new() { Value = Account.PlayerUuid },
            new() { Value = "--accessToken" },
            new() { Value = "0" },
            new() { Value = "--userType" },
            new() { Value = "legacy" },
            new() { Value = "--versionType" },
            new() { Value = "release" },
            new() { Value = "--userProperties" },
            new() { Value = "{}" }
        };

        if (AssetsDirectory is not null)
        {
            GameArguments.Add(new LaunchArgumentDto { Value = "--assetsDir" });
            GameArguments.Add(new LaunchArgumentDto { Value = AssetsDirectory });
            GameArguments.Add(new LaunchArgumentDto { Value = "--assetIndex" });
            GameArguments.Add(new LaunchArgumentDto { Value = AssetIndexId! });
        }

        foreach (var Arg in LaunchPlanTokenSupport.ResolveRuntimeArguments(RuntimeArguments, TokenMap))
        {
            GameArguments.Add(new LaunchArgumentDto { Value = Arg });
        }

        if (Instance.LoaderType != LoaderType.Vanilla)
        {
            var LoaderVersionText = Instance.LoaderVersion?.ToString();
            if (string.IsNullOrWhiteSpace(LoaderVersionText))
            {
                throw new InvalidOperationException("Loader version was missing for a modded launch instance.");
            }

            GameArguments.Add(new LaunchArgumentDto { Value = "--loader" });
            GameArguments.Add(new LaunchArgumentDto { Value = Instance.LoaderType.ToString() });
            GameArguments.Add(new LaunchArgumentDto { Value = "--loaderVersion" });
            GameArguments.Add(new LaunchArgumentDto { Value = LoaderVersionText });
        }

        foreach (var Arg in Instance.LaunchProfile.ExtraGameArgs)
        {
            if (!string.IsNullOrWhiteSpace(Arg))
            {
                GameArguments.Add(new LaunchArgumentDto
                {
                    Value = LaunchPlanTokenSupport.ExpandTokens(Arg, TokenMap)
                });
            }
        }

        return GameArguments;
    }

    internal static List<LaunchEnvironmentVariableDto> BuildEnvironmentVariables(
        LauncherInstance Instance,
        IReadOnlyDictionary<string, string> TokenMap)
    {
        return Instance.LaunchProfile.EnvironmentVariables
            .Select(x => new LaunchEnvironmentVariableDto
            {
                Name = x.Key,
                Value = LaunchPlanTokenSupport.ExpandTokens(x.Value, TokenMap)
            })
            .ToList();
    }
}
