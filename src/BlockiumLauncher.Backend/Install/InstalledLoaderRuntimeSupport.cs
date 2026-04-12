using System.Diagnostics;
using System.Text.Json;
using BlockiumLauncher.Application.Abstractions.Diagnostics;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using static BlockiumLauncher.Infrastructure.Storage.LegacyLoaderRuntimeCommon;

namespace BlockiumLauncher.Infrastructure.Storage;

internal sealed class InstalledLoaderRuntimeSupport
{
    private readonly string SourceName;
    private readonly IStructuredLogger Logger;
    private readonly IJavaRuntimeResolver JavaRuntimeResolver;
    private readonly LauncherPaths LauncherPaths;

    public InstalledLoaderRuntimeSupport(
        string SourceName,
        IStructuredLogger Logger,
        IJavaRuntimeResolver JavaRuntimeResolver,
        LauncherPaths LauncherPaths)
    {
        this.SourceName = string.IsNullOrWhiteSpace(SourceName) ? nameof(InstalledLoaderRuntimeSupport) : SourceName;
        this.Logger = Logger ?? throw new ArgumentNullException(nameof(Logger));
        this.JavaRuntimeResolver = JavaRuntimeResolver ?? throw new ArgumentNullException(nameof(JavaRuntimeResolver));
        this.LauncherPaths = LauncherPaths ?? throw new ArgumentNullException(nameof(LauncherPaths));
    }

    public async Task<string> DownloadAndInstallAsync(
        InstallPlan Plan,
        LoaderType LoaderType,
        string InstallerUrl,
        string InstallerFileName,
        string InstallCommand,
        OperationContext Context,
        CancellationToken CancellationToken)
    {
        var JavaResolveResult = await JavaRuntimeResolver.ResolveExecutablePathAsync(
            Plan.GameVersion,
            LoaderType,
            preferredJavaMajor: null,
            skipCompatibilityChecks: false,
            CancellationToken).ConfigureAwait(false);
        if (JavaResolveResult.IsFailure)
        {
            throw new InvalidOperationException($"Could not resolve Java for {LoaderType} installer.");
        }

        var JavaPath = JavaResolveResult.Value;
        var LoaderRoot = LauncherPaths.GetSharedLoaderDirectory(LoaderType, Plan.GameVersion, Plan.LoaderVersion!);
        var InstallerPath = Path.Combine(LoaderRoot, InstallerFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(InstallerPath)!);

        using (var HttpClient = new HttpClient())
        {
            await DownloadFileAsync(HttpClient, InstallerUrl, InstallerPath, null, CancellationToken).ConfigureAwait(false);
        }

        var RuntimeRoot = Path.Combine(LoaderRoot, "runtime");
        Directory.CreateDirectory(RuntimeRoot);

        EnsureLauncherProfiles(RuntimeRoot, CancellationToken);

        var StartInfo = new ProcessStartInfo
        {
            FileName = JavaPath,
            WorkingDirectory = RuntimeRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        StartInfo.ArgumentList.Add("-jar");
        StartInfo.ArgumentList.Add(InstallerPath);
        StartInfo.ArgumentList.Add(InstallCommand);
        StartInfo.ArgumentList.Add(".");

        Logger.Info(Context, SourceName, $"{LoaderType}InstallerStart", $"Starting {LoaderType} installer.", new
        {
            JavaPath,
            InstallerPath,
            WorkingDirectory = RuntimeRoot,
            Arguments = StartInfo.ArgumentList.ToArray(),
            Plan.GameVersion,
            Plan.LoaderVersion
        });

        using var Process = new Process { StartInfo = StartInfo };

        try
        {
            Process.Start();
        }
        catch (Exception Exception)
        {
            Logger.Error(Context, SourceName, $"{LoaderType}InstallerStartFailed", $"Failed to start {LoaderType} installer process.", new
            {
                JavaPath,
                InstallerPath,
                WorkingDirectory = RuntimeRoot,
                Arguments = StartInfo.ArgumentList.ToArray()
            }, Exception);

            throw;
        }

        var StdOutTask = Process.StandardOutput.ReadToEndAsync(CancellationToken);
        var StdErrTask = Process.StandardError.ReadToEndAsync(CancellationToken);

        await Process.WaitForExitAsync(CancellationToken).ConfigureAwait(false);

        var StdOut = await StdOutTask.ConfigureAwait(false);
        var StdErr = await StdErrTask.ConfigureAwait(false);

        Logger.Info(Context, SourceName, $"{LoaderType}InstallerFinished", $"{LoaderType} installer process finished.", new
        {
            ExitCode = Process.ExitCode,
            JavaPath,
            InstallerPath,
            WorkingDirectory = RuntimeRoot,
            Arguments = StartInfo.ArgumentList.ToArray(),
            StdOut,
            StdErr
        });

        if (Process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{LoaderType} installer failed." + Environment.NewLine +
                "ExitCode: " + Process.ExitCode + Environment.NewLine +
                "JavaPath: " + JavaPath + Environment.NewLine +
                "InstallerPath: " + InstallerPath + Environment.NewLine +
                "WorkingDirectory: " + RuntimeRoot + Environment.NewLine +
                "Arguments: " + string.Join(" ", StartInfo.ArgumentList.Select(static value => value.Contains(' ') ? "\"" + value + "\"" : value)) + Environment.NewLine +
                "StdOut:" + Environment.NewLine + StdOut + Environment.NewLine +
                "StdErr:" + Environment.NewLine + StdErr);
        }

        return RuntimeRoot;
    }

    public async Task BuildInstalledRuntimeMetadataAsync(
        InstallPlan Plan,
        LoaderType LoaderType,
        string RootPath,
        string RuntimeRoot,
        string PersistedVersionJsonFileName,
        OperationContext Context,
        CancellationToken CancellationToken)
    {
        var RuntimeMetadataPath = Path.Combine(RootPath, ".blockium", "runtime.json");
        var RuntimeMetadata = await ReadRuntimeMetadataAsync(RuntimeMetadataPath, CancellationToken).ConfigureAwait(false);

        var VersionsRoot = Path.Combine(RuntimeRoot, "versions");
        if (!Directory.Exists(VersionsRoot))
        {
            throw new InvalidOperationException($"{LoaderType} installer did not create a versions directory.");
        }

        var CandidateJsonFiles = Directory
            .EnumerateFiles(VersionsRoot, "*.json", SearchOption.AllDirectories)
            .OrderByDescending(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var SearchText = LoaderType == LoaderType.NeoForge ? "neoforge" : "forge";
        var VersionJsonPath = CandidateJsonFiles.FirstOrDefault(PathValue =>
            Path.GetFileNameWithoutExtension(PathValue).Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(PathValue).Contains(Plan.LoaderVersion!, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(VersionJsonPath))
        {
            throw new InvalidOperationException($"Could not find installed {LoaderType} version json.");
        }

        Logger.Info(Context, SourceName, $"{LoaderType}MetadataSourceResolved", $"Resolved {LoaderType} metadata source files.", new
        {
            RuntimeMetadataPath,
            RuntimeRoot,
            VersionsRoot,
            VersionJsonPath,
            CandidateJsonFiles,
            LoaderVersion = Plan.LoaderVersion
        });

        var VersionJson = await File.ReadAllTextAsync(VersionJsonPath, CancellationToken).ConfigureAwait(false);
        using var VersionDocument = JsonDocument.Parse(VersionJson);
        var Root = VersionDocument.RootElement;

        var MainClass = Root.TryGetProperty("mainClass", out var MainClassElement) && MainClassElement.ValueKind == JsonValueKind.String
            ? MainClassElement.GetString()
            : RuntimeMetadata.MainClass;

        var ExtraJvmArguments = ExtractArguments(Root, "jvm");
        var ExtraGameArguments = ExtractArguments(Root, "game");

        if (ExtraGameArguments.Length == 0 &&
            Root.TryGetProperty("minecraftArguments", out var MinecraftArgumentsElement) &&
            MinecraftArgumentsElement.ValueKind == JsonValueKind.String)
        {
            ExtraGameArguments = MinecraftArgumentsElement
                .GetString()!
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        var LibrariesRoot = Path.Combine(RuntimeRoot, "libraries");
        var LoaderClasspath = ResolveLoaderClasspath(Root, LibrariesRoot);

        var VersionJarPath = Path.ChangeExtension(VersionJsonPath, ".jar");
        if (File.Exists(VersionJarPath))
        {
            LoaderClasspath.Add(VersionJarPath);
        }

        var CombinedClasspath = new List<string>();
        CombinedClasspath.AddRange(LoaderClasspath);

        foreach (var Existing in RuntimeMetadata.ClasspathEntries)
        {
            if (!CombinedClasspath.Contains(Existing, StringComparer.OrdinalIgnoreCase))
            {
                CombinedClasspath.Add(Existing);
            }
        }

        RuntimeMetadata.MainClass = MainClass ?? RuntimeMetadata.MainClass;
        RuntimeMetadata.ClasspathEntries = CombinedClasspath.ToArray();
        RuntimeMetadata.ExtraJvmArguments = MergePreservingOrder(RuntimeMetadata.ExtraJvmArguments, ExtraJvmArguments);
        RuntimeMetadata.ExtraGameArguments = MergePreservingOrder(RuntimeMetadata.ExtraGameArguments, ExtraGameArguments);
        RuntimeMetadata.LoaderType = LoaderType.ToString();
        RuntimeMetadata.LoaderVersion = Plan.LoaderVersion ?? string.Empty;
        RuntimeMetadata.LoaderProfileJsonPath = Path.Combine(RuntimeRoot, PersistedVersionJsonFileName);

        var PersistedVersionJsonPath = Path.Combine(RuntimeRoot, PersistedVersionJsonFileName);
        await File.WriteAllTextAsync(PersistedVersionJsonPath, VersionJson, CancellationToken).ConfigureAwait(false);

        await WriteRuntimeMetadataAsync(RuntimeMetadataPath, RuntimeMetadata, CancellationToken).ConfigureAwait(false);
    }

    private static void EnsureLauncherProfiles(string RuntimeRoot, CancellationToken CancellationToken)
    {
        var LauncherProfilesPath = Path.Combine(RuntimeRoot, "launcher_profiles.json");
        if (File.Exists(LauncherProfilesPath))
        {
            return;
        }

        var LauncherProfilesJson = """
{
  "profiles": {},
  "settings": {},
  "version": 3
}
""";
        File.WriteAllText(LauncherProfilesPath, LauncherProfilesJson);
        CancellationToken.ThrowIfCancellationRequested();
    }

    private static string[] ExtractArguments(JsonElement Root, string Kind)
    {
        if (!Root.TryGetProperty("arguments", out var ArgumentsElement) || ArgumentsElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        if (!ArgumentsElement.TryGetProperty(Kind, out var KindElement) || KindElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var Values = new List<string>();

        foreach (var Entry in KindElement.EnumerateArray())
        {
            if (Entry.ValueKind == JsonValueKind.String)
            {
                var Value = Entry.GetString();
                if (!string.IsNullOrWhiteSpace(Value))
                {
                    Values.Add(Value);
                }
            }
        }

        return Values.ToArray();
    }

    private static List<string> ResolveLoaderClasspath(JsonElement Root, string LibrariesRoot)
    {
        var LoaderClasspath = new List<string>();

        if (Root.TryGetProperty("libraries", out var LibrariesElement) && LibrariesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var Library in LibrariesElement.EnumerateArray())
            {
                if (!Library.TryGetProperty("downloads", out var DownloadsElement))
                {
                    continue;
                }

                if (!DownloadsElement.TryGetProperty("artifact", out var ArtifactElement))
                {
                    continue;
                }

                var RelativePath = ArtifactElement.TryGetProperty("path", out var PathElement) && PathElement.ValueKind == JsonValueKind.String
                    ? PathElement.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(RelativePath))
                {
                    continue;
                }

                var FullPath = Path.Combine(LibrariesRoot, RelativePath);
                if (File.Exists(FullPath))
                {
                    LoaderClasspath.Add(FullPath);
                }
            }
        }

        return LoaderClasspath;
    }
}
