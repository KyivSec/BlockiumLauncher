using BlockiumLauncher.Application.Abstractions.Launch;

namespace BlockiumLauncher.Application.UseCases.Launch;

internal static class LaunchPlanRuntimeSupport
{
    internal static string? ResolveMainClass(BuildLaunchPlanRequest Request, RuntimeMetadata? RuntimeMetadata)
    {
        if (!string.IsNullOrWhiteSpace(Request.MainClass))
        {
            return Request.MainClass.Trim();
        }

        if (!string.IsNullOrWhiteSpace(RuntimeMetadata?.MainClass))
        {
            return RuntimeMetadata.MainClass.Trim();
        }

        return null;
    }

    internal static List<string> ResolveClasspathEntries(
        BuildLaunchPlanRequest Request,
        RuntimeMetadata? RuntimeMetadata,
        string WorkingDirectory)
    {
        var SourceEntries = Request.ClasspathEntries is not null && Request.ClasspathEntries.Count > 0
            ? Request.ClasspathEntries
            : RuntimeMetadata?.ClasspathEntries ?? [];

        var Result = new List<string>();

        foreach (var Entry in SourceEntries)
        {
            if (string.IsNullOrWhiteSpace(Entry))
            {
                continue;
            }

            var FullEntry = Path.IsPathRooted(Entry)
                ? Path.GetFullPath(Entry)
                : Path.GetFullPath(Path.Combine(WorkingDirectory, Entry));

            if (!File.Exists(FullEntry) && !Directory.Exists(FullEntry))
            {
                continue;
            }

            if (!Result.Contains(FullEntry, StringComparer.OrdinalIgnoreCase))
            {
                Result.Add(FullEntry);
            }
        }

        return Result;
    }

    internal static string? ResolveAssetsDirectory(
        BuildLaunchPlanRequest Request,
        RuntimeMetadata? RuntimeMetadata,
        string WorkingDirectory)
    {
        var Value = !string.IsNullOrWhiteSpace(Request.AssetsDirectory)
            ? Request.AssetsDirectory
            : RuntimeMetadata?.AssetsDirectory;

        if (string.IsNullOrWhiteSpace(Value))
        {
            return null;
        }

        return Path.IsPathRooted(Value)
            ? Path.GetFullPath(Value)
            : Path.GetFullPath(Path.Combine(WorkingDirectory, Value));
    }

    internal static string? ResolveAssetIndexId(BuildLaunchPlanRequest Request, RuntimeMetadata? RuntimeMetadata)
    {
        if (!string.IsNullOrWhiteSpace(Request.AssetIndexId))
        {
            return Request.AssetIndexId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(RuntimeMetadata?.AssetIndexId))
        {
            return RuntimeMetadata.AssetIndexId.Trim();
        }

        return null;
    }

    internal static string? ResolveNativesDirectory(RuntimeMetadata? RuntimeMetadata, string WorkingDirectory)
    {
        if (string.IsNullOrWhiteSpace(RuntimeMetadata?.NativesDirectory))
        {
            return null;
        }

        var FullPath = Path.IsPathRooted(RuntimeMetadata.NativesDirectory)
            ? Path.GetFullPath(RuntimeMetadata.NativesDirectory)
            : Path.GetFullPath(Path.Combine(WorkingDirectory, RuntimeMetadata.NativesDirectory));

        return Directory.Exists(FullPath) ? FullPath : null;
    }

    internal static string TryResolveLibraryDirectory(
        IReadOnlyList<string> ClasspathEntries,
        RuntimeMetadata? RuntimeMetadata)
    {
        if (!string.IsNullOrWhiteSpace(RuntimeMetadata?.LibraryDirectory))
        {
            return RuntimeMetadata.LibraryDirectory;
        }

        foreach (var Entry in ClasspathEntries)
        {
            var DirectoryPath = File.Exists(Entry) ? Path.GetDirectoryName(Entry) : Entry;
            if (string.IsNullOrWhiteSpace(DirectoryPath))
            {
                continue;
            }

            var Current = new DirectoryInfo(DirectoryPath);
            while (Current is not null)
            {
                if (string.Equals(Current.Name, "libraries", StringComparison.OrdinalIgnoreCase))
                {
                    return Current.FullName;
                }

                Current = Current.Parent;
            }
        }

        return string.Empty;
    }
}
