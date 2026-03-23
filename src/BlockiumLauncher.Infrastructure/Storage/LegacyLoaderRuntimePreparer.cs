using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Diagnostics;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.Diagnostics;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Storage;

public sealed class LegacyLoaderRuntimePreparer : ILoaderRuntimePreparer
{
    private const int MaxParallelLibraryDownloads = 8;
    private const int MaxParallelAssetDownloads = 16;
    private static readonly Uri VanillaManifestUri = new("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");

    private readonly IStructuredLogger Logger;
    private readonly IOperationContextFactory OperationContextFactory;
    private readonly IJavaRuntimeResolver JavaRuntimeResolver;
    private readonly LauncherPaths LauncherPaths;

    public LegacyLoaderRuntimePreparer()
        : this(
            NullStructuredLogger.Instance,
            DefaultOperationContextFactory.Instance,
            NoOpJavaRuntimeResolver.Instance,
            LauncherPaths.CreateDefault())
    {
    }

    public LegacyLoaderRuntimePreparer(
        IStructuredLogger Logger,
        IOperationContextFactory OperationContextFactory,
        IJavaRuntimeResolver JavaRuntimeResolver,
        ILauncherPaths LauncherPaths)
    {
        this.Logger = Logger ?? throw new ArgumentNullException(nameof(Logger));
        this.OperationContextFactory = OperationContextFactory ?? throw new ArgumentNullException(nameof(OperationContextFactory));
        this.JavaRuntimeResolver = JavaRuntimeResolver ?? throw new ArgumentNullException(nameof(JavaRuntimeResolver));
        this.LauncherPaths = LauncherPaths as LauncherPaths ?? throw new ArgumentNullException(nameof(LauncherPaths));
    }

    public bool CanPrepare(LoaderType loaderType)
{
 return loaderType == LoaderType.Vanilla;
}

public async Task<Result<string>> PrepareAsync(
        InstallPlan Plan,
        ITempWorkspace Workspace,
        CancellationToken CancellationToken = default)
    {
        var Context = OperationContextFactory.Create("PrepareInstanceContent");

        try
        {
            CancellationToken.ThrowIfCancellationRequested();

            Logger.Info(Context, nameof(LegacyLoaderRuntimePreparer), "PrepareStarted", "Preparing instance content.", new
            {
                Plan.InstanceName,
                Plan.GameVersion,
                LoaderType = Plan.LoaderType.ToString(),
                Plan.LoaderVersion,
                Plan.DownloadRuntime
            });

            var RootPath = Workspace.GetPath("instance-root");
            Directory.CreateDirectory(RootPath);

            Logger.Info(Context, nameof(LegacyLoaderRuntimePreparer), "WorkspaceResolved", "Resolved temporary instance root.", new
            {
                RootPath
            });

            foreach (var Step in Plan.Steps)
            {
                CancellationToken.ThrowIfCancellationRequested();

                if (Step.Kind == InstallPlanStepKind.CreateDirectory)
                {
                    Directory.CreateDirectory(Path.Combine(RootPath, Step.Destination));
                    continue;
                }

                if (Step.Kind == InstallPlanStepKind.WriteMetadata)
                {
                    var FilePath = Path.Combine(RootPath, Step.Destination);
                    var ParentDirectory = Path.GetDirectoryName(FilePath);
                    if (!string.IsNullOrWhiteSpace(ParentDirectory))
                    {
                        Directory.CreateDirectory(ParentDirectory);
                    }

                    var Payload = new
                    {
                        Plan.InstanceName,
                        Plan.GameVersion,
                        LoaderType = Plan.LoaderType.ToString(),
                        Plan.LoaderVersion,
                        Plan.TargetDirectory,
                        Plan.DownloadRuntime,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    await File.WriteAllTextAsync(
                        FilePath,
                        JsonSerializer.Serialize(Payload, new JsonSerializerOptions { WriteIndented = true }),
                        CancellationToken).ConfigureAwait(false);
                }
            }

            if (Plan.DownloadRuntime)
            {
                switch (Plan.LoaderType)
                {
                    case LoaderType.Vanilla:
                        await DownloadVanillaRuntimeAsync(Plan, RootPath, Context, CancellationToken).ConfigureAwait(false);
                        break;

                    case LoaderType.Fabric:
                        if (string.IsNullOrWhiteSpace(Plan.LoaderVersion))
                        {
                            return Result<string>.Failure(InstallErrors.InvalidRequest);
                        }

                        await DownloadVanillaRuntimeAsync(Plan, RootPath, Context, CancellationToken).ConfigureAwait(false);
                        await DownloadFabricRuntimeAsync(Plan, RootPath, Context, CancellationToken).ConfigureAwait(false);
                        break;

                    case LoaderType.NeoForge:
                        if (string.IsNullOrWhiteSpace(Plan.LoaderVersion))
                        {
                            return Result<string>.Failure(InstallErrors.InvalidRequest);
                        }

                        await DownloadVanillaRuntimeAsync(Plan, RootPath, Context, CancellationToken).ConfigureAwait(false);
                        await DownloadNeoForgeRuntimeAsync(Plan, RootPath, Context, CancellationToken).ConfigureAwait(false);
                        break;

                    default:
                        return Result<string>.Failure(InstallErrors.InvalidRequest);
                }
            }

            var MarkerPath = Path.Combine(RootPath, "instance.json");
            var MarkerPayload = new
            {
                Name = Plan.InstanceName,
                Version = Plan.GameVersion,
                LoaderType = Plan.LoaderType.ToString(),
                Plan.LoaderVersion,
                Plan.DownloadRuntime,
                InstalledBy = "BlockiumLauncher.SharedStorage"
            };

            await File.WriteAllTextAsync(
                MarkerPath,
                JsonSerializer.Serialize(MarkerPayload, new JsonSerializerOptions { WriteIndented = true }),
                CancellationToken).ConfigureAwait(false);

            return Result<string>.Success(RootPath);
        }
        catch (Exception Exception)
        {
            Logger.Error(Context, nameof(LegacyLoaderRuntimePreparer), "PrepareFailed", "Instance content preparation failed.", new
            {
                Plan.InstanceName,
                Plan.GameVersion,
                LoaderType = Plan.LoaderType.ToString(),
                Plan.LoaderVersion
            }, Exception);

            return Result<string>.Failure(InstallErrors.DownloadFailed);
        }
    }

    private async Task DownloadVanillaRuntimeAsync(
        InstallPlan Plan,
        string RootPath,
        OperationContext Context,
        CancellationToken CancellationToken)
    {
        using var HttpClient = new HttpClient();

        var VersionDirectory = LauncherPaths.GetSharedVersionDirectory(Plan.GameVersion);
        var ClientJarPath = LauncherPaths.GetSharedClientJarPath(Plan.GameVersion);
        var VersionJsonPath = LauncherPaths.GetSharedVersionJsonPath(Plan.GameVersion);
        var LibrariesRoot = LauncherPaths.SharedLibrariesDirectory;
        var AssetsRoot = LauncherPaths.SharedAssetsDirectory;
        var AssetsIndexesRoot = LauncherPaths.SharedAssetIndexesDirectory;
        var AssetsObjectsRoot = LauncherPaths.SharedAssetObjectsDirectory;
        var NativesRoot = LauncherPaths.GetSharedNativesDirectory("vanilla-" + Plan.GameVersion);

        Directory.CreateDirectory(VersionDirectory);
        Directory.CreateDirectory(LibrariesRoot);
        Directory.CreateDirectory(AssetsIndexesRoot);
        Directory.CreateDirectory(AssetsObjectsRoot);
        Directory.CreateDirectory(NativesRoot);

        var ManifestJson = await HttpClient.GetStringAsync(VanillaManifestUri, CancellationToken).ConfigureAwait(false);
        using var ManifestDocument = JsonDocument.Parse(ManifestJson);

        var VersionNode = ManifestDocument.RootElement
            .GetProperty("versions")
            .EnumerateArray()
            .FirstOrDefault(Item =>
                string.Equals(Item.GetProperty("id").GetString(), Plan.GameVersion, StringComparison.OrdinalIgnoreCase));

        var VersionUrl = VersionNode.ValueKind == JsonValueKind.Undefined
            ? null
            : VersionNode.GetProperty("url").GetString();

        if (string.IsNullOrWhiteSpace(VersionUrl))
        {
            throw new InvalidOperationException("Could not resolve version metadata URL.");
        }

        var VersionJson = await HttpClient.GetStringAsync(VersionUrl, CancellationToken).ConfigureAwait(false);
        using var VersionDocument = JsonDocument.Parse(VersionJson);
        var Root = VersionDocument.RootElement;

        await File.WriteAllTextAsync(VersionJsonPath, VersionJson, CancellationToken).ConfigureAwait(false);

        var ClientDownload = Root.GetProperty("downloads").GetProperty("client");
        var ClientUrl = ClientDownload.GetProperty("url").GetString()!;
        var ClientSha1 = ClientDownload.TryGetProperty("sha1", out var ClientSha1Element) ? ClientSha1Element.GetString() : null;

        await DownloadFileAsync(HttpClient, ClientUrl, ClientJarPath, ClientSha1, CancellationToken).ConfigureAwait(false);

        var ClasspathEntries = new System.Collections.Generic.List<string>();

        if (Root.TryGetProperty("libraries", out var LibrariesElement) && LibrariesElement.ValueKind == JsonValueKind.Array)
        {
            var DownloadedClasspathEntries = await DownloadLibrariesInParallelAsync(
                LibrariesElement,
                LibrariesRoot,
                RootPath,
                Context,
                CancellationToken).ConfigureAwait(false);

            foreach (var Entry in DownloadedClasspathEntries)
            {
                ClasspathEntries.Add(Entry);
            }

            var NativeArchives = Directory.Exists(LibrariesRoot)
                ? Directory.EnumerateFiles(LibrariesRoot, "*.jar", SearchOption.AllDirectories)
                    .Where(PathValue =>
                        PathValue.Contains("natives", StringComparison.OrdinalIgnoreCase) ||
                        PathValue.Contains("windows", StringComparison.OrdinalIgnoreCase) ||
                        PathValue.Contains("linux", StringComparison.OrdinalIgnoreCase) ||
                        PathValue.Contains("osx", StringComparison.OrdinalIgnoreCase))
                : Enumerable.Empty<string>();

            foreach (var NativeArchivePath in NativeArchives)
            {
                ExtractNativeArchive(NativeArchivePath, NativesRoot);
            }
        }

        ClasspathEntries.Add(ClientJarPath);

        string? AssetIndexId = null;
        if (Root.TryGetProperty("assetIndex", out var AssetIndexElement))
        {
            AssetIndexId = AssetIndexElement.GetProperty("id").GetString();
            var AssetIndexUrl = AssetIndexElement.GetProperty("url").GetString();

            if (!string.IsNullOrWhiteSpace(AssetIndexId) && !string.IsNullOrWhiteSpace(AssetIndexUrl))
            {
                var AssetIndexPath = Path.Combine(AssetsIndexesRoot, AssetIndexId + ".json");
                var AssetIndexSha1 = AssetIndexElement.TryGetProperty("sha1", out var AssetIndexSha1Element) ? AssetIndexSha1Element.GetString() : null;

                await DownloadFileAsync(HttpClient, AssetIndexUrl, AssetIndexPath, AssetIndexSha1, CancellationToken).ConfigureAwait(false);

                var AssetIndexJson = await File.ReadAllTextAsync(AssetIndexPath, CancellationToken).ConfigureAwait(false);
                using var AssetIndexDocument = JsonDocument.Parse(AssetIndexJson);

                if (AssetIndexDocument.RootElement.TryGetProperty("objects", out var ObjectsElement))
                {
                    await DownloadAssetsInParallelAsync(
                        ObjectsElement,
                        AssetsObjectsRoot,
                        Context,
                        CancellationToken).ConfigureAwait(false);
                }
            }
        }

        var MainClass = Root.TryGetProperty("mainClass", out var MainClassElement)
            ? MainClassElement.GetString()
            : null;

        var RuntimeMetadataPath = Path.Combine(RootPath, ".blockium", "runtime.json");
        Directory.CreateDirectory(Path.GetDirectoryName(RuntimeMetadataPath)!);

        var RuntimeMetadata = new RuntimeMetadataFile
        {
            Version = Plan.GameVersion,
            MainClass = MainClass ?? string.Empty,
            ClientJarPath = ClientJarPath,
            ClasspathEntries = ClasspathEntries.ToArray(),
            AssetsDirectory = AssetsRoot,
            AssetIndexId = AssetIndexId ?? string.Empty,
            NativesDirectory = NativesRoot,
            ExtraJvmArguments = [],
            ExtraGameArguments = []
        };

        await WriteRuntimeMetadataAsync(RuntimeMetadataPath, RuntimeMetadata, CancellationToken).ConfigureAwait(false);
    }

    private async Task DownloadFabricRuntimeAsync(
        InstallPlan Plan,
        string RootPath,
        OperationContext Context,
        CancellationToken CancellationToken)
    {
        using var HttpClient = new HttpClient();

        var ProfileUrl = $"https://meta.fabricmc.net/v2/versions/loader/{Plan.GameVersion}/{Plan.LoaderVersion}/profile/json";
        var ProfileJson = await HttpClient.GetStringAsync(ProfileUrl, CancellationToken).ConfigureAwait(false);
        using var ProfileDocument = JsonDocument.Parse(ProfileJson);
        var Root = ProfileDocument.RootElement;

        var RuntimeMetadataPath = Path.Combine(RootPath, ".blockium", "runtime.json");
        var RuntimeMetadata = await ReadRuntimeMetadataAsync(RuntimeMetadataPath, CancellationToken).ConfigureAwait(false);

        var FabricLoaderRoot = LauncherPaths.GetSharedLoaderDirectory(LoaderType.Fabric, Plan.GameVersion, Plan.LoaderVersion!);
        var FabricLibrariesRoot = Path.Combine(FabricLoaderRoot, "libraries");
        Directory.CreateDirectory(FabricLibrariesRoot);

        var FabricLibraries = new System.Collections.Generic.List<string>();

        if (Root.TryGetProperty("libraries", out var LibrariesElement) && LibrariesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var Library in LibrariesElement.EnumerateArray())
            {
                if (!Library.TryGetProperty("name", out var NameElement) || NameElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var Coordinates = NameElement.GetString();
                if (string.IsNullOrWhiteSpace(Coordinates))
                {
                    continue;
                }

                var RepositoryUrl = Library.TryGetProperty("url", out var UrlElement) && UrlElement.ValueKind == JsonValueKind.String
                    ? UrlElement.GetString()
                    : "https://maven.fabricmc.net/";

                if (string.IsNullOrWhiteSpace(RepositoryUrl))
                {
                    RepositoryUrl = "https://maven.fabricmc.net/";
                }

                var RelativeArtifactPath = BuildMavenArtifactPath(Coordinates);
                var DownloadUrl = CombineMavenUrl(RepositoryUrl, RelativeArtifactPath);
                var DestinationPath = Path.Combine(FabricLibrariesRoot, RelativeArtifactPath);

                await DownloadFileAsync(HttpClient, DownloadUrl, DestinationPath, null, CancellationToken).ConfigureAwait(false);

                FabricLibraries.Add(DestinationPath);
            }
        }

        var MainClass = Root.TryGetProperty("mainClass", out var MainClassElement) && MainClassElement.ValueKind == JsonValueKind.String
            ? MainClassElement.GetString()
            : RuntimeMetadata.MainClass;

        var JvmArgs = ExtractArguments(Root, "jvm");
        var GameArgs = ExtractArguments(Root, "game");

        var CombinedClasspath = new System.Collections.Generic.List<string>();
        CombinedClasspath.AddRange(FabricLibraries);
        foreach (var Existing in RuntimeMetadata.ClasspathEntries)
        {
            if (!CombinedClasspath.Contains(Existing, StringComparer.OrdinalIgnoreCase))
            {
                CombinedClasspath.Add(Existing);
            }
        }

        RuntimeMetadata.MainClass = MainClass ?? RuntimeMetadata.MainClass;
        RuntimeMetadata.ClasspathEntries = CombinedClasspath.ToArray();
        RuntimeMetadata.ExtraJvmArguments = MergeDistinct(RuntimeMetadata.ExtraJvmArguments, JvmArgs);
        RuntimeMetadata.ExtraGameArguments = MergeDistinct(RuntimeMetadata.ExtraGameArguments, GameArgs);
        RuntimeMetadata.LoaderType = LoaderType.Fabric.ToString();
        RuntimeMetadata.LoaderVersion = Plan.LoaderVersion ?? string.Empty;
        RuntimeMetadata.LoaderProfileJsonPath = Path.Combine(FabricLoaderRoot, "fabric-profile.json");

        var ProfilePath = Path.Combine(FabricLoaderRoot, "fabric-profile.json");
        Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
        await File.WriteAllTextAsync(ProfilePath, ProfileJson, CancellationToken).ConfigureAwait(false);

        await WriteRuntimeMetadataAsync(RuntimeMetadataPath, RuntimeMetadata, CancellationToken).ConfigureAwait(false);
    }

    private async Task DownloadNeoForgeRuntimeAsync(
        InstallPlan Plan,
        string RootPath,
        OperationContext Context,
        CancellationToken CancellationToken)
    {
        var JavaResolveResult = await JavaRuntimeResolver.ResolveExecutablePathAsync(Plan.GameVersion, CancellationToken).ConfigureAwait(false);
        if (JavaResolveResult.IsFailure)
        {
            throw new InvalidOperationException("Could not resolve Java for NeoForge installer.");
        }

        var JavaPath = JavaResolveResult.Value;
        var NeoForgeRoot = LauncherPaths.GetSharedLoaderDirectory(LoaderType.NeoForge, Plan.GameVersion, Plan.LoaderVersion!);
        var InstallerUrl = $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{Plan.LoaderVersion}/neoforge-{Plan.LoaderVersion}-installer.jar";
        var InstallerPath = Path.Combine(NeoForgeRoot, $"neoforge-{Plan.LoaderVersion}-installer.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(InstallerPath)!);

        using (var HttpClient = new HttpClient())
        {
            await DownloadFileAsync(HttpClient, InstallerUrl, InstallerPath, null, CancellationToken).ConfigureAwait(false);
        }

        var MinecraftRoot = Path.Combine(NeoForgeRoot, "runtime");
        Directory.CreateDirectory(MinecraftRoot);

        var StartInfo = new ProcessStartInfo
        {
            FileName = JavaPath,
            WorkingDirectory = MinecraftRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        StartInfo.ArgumentList.Add("-jar");
        StartInfo.ArgumentList.Add(InstallerPath);
        StartInfo.ArgumentList.Add("--install-client");
        StartInfo.ArgumentList.Add(".");

        using var Process = new Process { StartInfo = StartInfo };
        Process.Start();

        var StdOutTask = Process.StandardOutput.ReadToEndAsync(CancellationToken);
        var StdErrTask = Process.StandardError.ReadToEndAsync(CancellationToken);

        await Process.WaitForExitAsync(CancellationToken).ConfigureAwait(false);

        var StdErr = await StdErrTask.ConfigureAwait(false);
        await StdOutTask.ConfigureAwait(false);

        if (Process.ExitCode != 0)
        {
            throw new InvalidOperationException("NeoForge installer failed: " + StdErr);
        }

        await BuildNeoForgeRuntimeMetadataAsync(Plan, RootPath, MinecraftRoot, CancellationToken).ConfigureAwait(false);
    }

    private async Task BuildNeoForgeRuntimeMetadataAsync(
        InstallPlan Plan,
        string RootPath,
        string NeoForgeRuntimeRoot,
        CancellationToken CancellationToken)
    {
        var RuntimeMetadataPath = Path.Combine(RootPath, ".blockium", "runtime.json");
        var RuntimeMetadata = await ReadRuntimeMetadataAsync(RuntimeMetadataPath, CancellationToken).ConfigureAwait(false);

        var VersionsRoot = Path.Combine(NeoForgeRuntimeRoot, "versions");
        if (!Directory.Exists(VersionsRoot))
        {
            throw new InvalidOperationException("NeoForge installer did not create a versions directory.");
        }

        var CandidateJsonFiles = Directory
            .EnumerateFiles(VersionsRoot, "*.json", SearchOption.AllDirectories)
            .OrderByDescending(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var VersionJsonPath = CandidateJsonFiles.FirstOrDefault(PathValue =>
            Path.GetFileNameWithoutExtension(PathValue).Contains("neoforge", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(PathValue).Contains(Plan.LoaderVersion!, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(VersionJsonPath))
        {
            throw new InvalidOperationException("Could not find installed NeoForge version json.");
        }

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

        var LibrariesRoot = Path.Combine(NeoForgeRuntimeRoot, "libraries");
        var LoaderClasspath = new System.Collections.Generic.List<string>();

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

        var VersionJarPath = Path.ChangeExtension(VersionJsonPath, ".jar");
        if (File.Exists(VersionJarPath))
        {
            LoaderClasspath.Add(VersionJarPath);
        }

        var CombinedClasspath = new System.Collections.Generic.List<string>();
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
        RuntimeMetadata.ExtraJvmArguments = MergeDistinct(RuntimeMetadata.ExtraJvmArguments, ExtraJvmArguments);
        RuntimeMetadata.ExtraGameArguments = MergeDistinct(RuntimeMetadata.ExtraGameArguments, ExtraGameArguments);
        RuntimeMetadata.LoaderType = LoaderType.NeoForge.ToString();
        RuntimeMetadata.LoaderVersion = Plan.LoaderVersion ?? string.Empty;
        RuntimeMetadata.LoaderProfileJsonPath = Path.Combine(NeoForgeRuntimeRoot, "neoforge-version.json");

        var PersistedVersionJsonPath = Path.Combine(NeoForgeRuntimeRoot, "neoforge-version.json");
        await File.WriteAllTextAsync(PersistedVersionJsonPath, VersionJson, CancellationToken).ConfigureAwait(false);

        await WriteRuntimeMetadataAsync(RuntimeMetadataPath, RuntimeMetadata, CancellationToken).ConfigureAwait(false);
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

        var Values = new System.Collections.Generic.List<string>();

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

    private static string[] MergeDistinct(string[] Existing, string[] Additional)
    {
        return Existing
            .Concat(Additional)
            .Where(Value => !string.IsNullOrWhiteSpace(Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildMavenArtifactPath(string Coordinates)
    {
        var Parts = Coordinates.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (Parts.Length < 3)
        {
            throw new InvalidOperationException("Invalid Maven coordinates: " + Coordinates);
        }

        var GroupId = Parts[0];
        var ArtifactId = Parts[1];
        var Version = Parts[2];
        var Classifier = Parts.Length >= 4 ? Parts[3] : null;
        var Extension = "jar";

        var GroupPath = GroupId.Replace('.', '/');
        var FileName = string.IsNullOrWhiteSpace(Classifier)
            ? $"{ArtifactId}-{Version}.{Extension}"
            : $"{ArtifactId}-{Version}-{Classifier}.{Extension}";

        return $"{GroupPath}/{ArtifactId}/{Version}/{FileName}";
    }

    private static string CombineMavenUrl(string BaseUrl, string RelativePath)
    {
        var NormalizedBase = BaseUrl.EndsWith("/", StringComparison.Ordinal) ? BaseUrl : BaseUrl + "/";
        return NormalizedBase + RelativePath;
    }

    private static async Task<RuntimeMetadataFile> ReadRuntimeMetadataAsync(string RuntimeMetadataPath, CancellationToken CancellationToken)
    {
        var Json = await File.ReadAllTextAsync(RuntimeMetadataPath, CancellationToken).ConfigureAwait(false);
        var Value = JsonSerializer.Deserialize<RuntimeMetadataFile>(Json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return Value ?? new RuntimeMetadataFile();
    }

    private static async Task WriteRuntimeMetadataAsync(string RuntimeMetadataPath, RuntimeMetadataFile RuntimeMetadata, CancellationToken CancellationToken)
    {
        var Json = JsonSerializer.Serialize(RuntimeMetadata, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(RuntimeMetadataPath, Json, CancellationToken).ConfigureAwait(false);
    }

    private static bool IsLibraryAllowed(JsonElement Library)
    {
        if (!Library.TryGetProperty("rules", out var RulesElement) || RulesElement.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        var Allowed = false;

        foreach (var Rule in RulesElement.EnumerateArray())
        {
            var Action = Rule.TryGetProperty("action", out var ActionElement) ? ActionElement.GetString() : null;
            var Applies = true;

            if (Rule.TryGetProperty("os", out var OsElement) && OsElement.TryGetProperty("name", out var OsNameElement))
            {
                var OsName = OsNameElement.GetString();
                Applies = IsCurrentOs(OsName);
            }

            if (!Applies)
            {
                continue;
            }

            if (string.Equals(Action, "allow", StringComparison.OrdinalIgnoreCase))
            {
                Allowed = true;
            }
            else if (string.Equals(Action, "disallow", StringComparison.OrdinalIgnoreCase))
            {
                Allowed = false;
            }
        }

        return Allowed;
    }

    private static string? GetNativeClassifierKey(JsonElement Library)
    {
        if (!Library.TryGetProperty("natives", out var NativesElement))
        {
            return null;
        }

        if (OperatingSystem.IsWindows() && NativesElement.TryGetProperty("windows", out var WindowsElement))
        {
            return WindowsElement.GetString();
        }

        if (OperatingSystem.IsLinux() && NativesElement.TryGetProperty("linux", out var LinuxElement))
        {
            return LinuxElement.GetString();
        }

        if (OperatingSystem.IsMacOS() && NativesElement.TryGetProperty("osx", out var OsxElement))
        {
            return OsxElement.GetString();
        }

        return null;
    }

    private static bool IsCurrentOs(string? OsName)
    {
        return (OperatingSystem.IsWindows() && string.Equals(OsName, "windows", StringComparison.OrdinalIgnoreCase))
            || (OperatingSystem.IsLinux() && string.Equals(OsName, "linux", StringComparison.OrdinalIgnoreCase))
            || (OperatingSystem.IsMacOS() && string.Equals(OsName, "osx", StringComparison.OrdinalIgnoreCase));
    }

    private static void ExtractNativeArchive(string ArchivePath, string DestinationDirectory)
    {
        using var Archive = ZipFile.OpenRead(ArchivePath);

        foreach (var Entry in Archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(Entry.Name))
            {
                continue;
            }

            var NormalizedPath = Entry.FullName.Replace('\\', '/');
            if (NormalizedPath.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var DestinationPath = Path.Combine(DestinationDirectory, Entry.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(DestinationPath)!);
            Entry.ExtractToFile(DestinationPath, true);
        }
    }

    private static async Task DownloadFileAsync(HttpClient HttpClient, string Url, string DestinationPath, string? Sha1, CancellationToken CancellationToken)
    {
        if (File.Exists(DestinationPath) && !string.IsNullOrWhiteSpace(Sha1))
        {
            var ExistingSha1 = await ComputeSha1Async(DestinationPath, CancellationToken).ConfigureAwait(false);
            if (string.Equals(ExistingSha1, Sha1, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var ParentDirectory = Path.GetDirectoryName(DestinationPath);
        if (!string.IsNullOrWhiteSpace(ParentDirectory))
        {
            Directory.CreateDirectory(ParentDirectory);
        }

        var TempPath = DestinationPath + ".tmp";
        if (File.Exists(TempPath))
        {
            File.Delete(TempPath);
        }

        await using (var Input = await HttpClient.GetStreamAsync(Url, CancellationToken).ConfigureAwait(false))
        await using (var Output = File.Create(TempPath))
        {
            await Input.CopyToAsync(Output, CancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(Sha1))
        {
            var ActualSha1 = await ComputeSha1Async(TempPath, CancellationToken).ConfigureAwait(false);
            if (!string.Equals(ActualSha1, Sha1, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(TempPath);
                throw new InvalidOperationException("Downloaded file hash mismatch.");
            }
        }

        if (File.Exists(DestinationPath))
        {
            File.Delete(DestinationPath);
        }

        File.Move(TempPath, DestinationPath);
    }

    private static async Task<string> ComputeSha1Async(string FilePath, CancellationToken CancellationToken)
    {
        await using var Stream = File.OpenRead(FilePath);
        using var Sha1 = SHA1.Create();
        var Hash = await Sha1.ComputeHashAsync(Stream, CancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(Hash).ToLowerInvariant();
    }


    private async Task<List<string>> DownloadLibrariesInParallelAsync(
        JsonElement LibrariesElement,
        string LibrariesRoot,
        string RootPath,
        OperationContext Context,
        CancellationToken CancellationToken)
    {
        var Results = new System.Collections.Concurrent.ConcurrentBag<string>();
        var WorkItems = new List<LibraryDownloadWorkItem>();

        foreach (var Library in LibrariesElement.EnumerateArray())
        {
            if (!IsLibraryAllowed(Library))
            {
                continue;
            }

            if (!Library.TryGetProperty("downloads", out var DownloadsElement))
            {
                continue;
            }

            if (DownloadsElement.TryGetProperty("artifact", out var ArtifactElement))
            {
                var PathValue = ArtifactElement.GetProperty("path").GetString();
                var UrlValue = ArtifactElement.GetProperty("url").GetString();

                if (!string.IsNullOrWhiteSpace(PathValue) && !string.IsNullOrWhiteSpace(UrlValue))
                {
                    var Sha1 = ArtifactElement.TryGetProperty("sha1", out var Sha1Element) ? Sha1Element.GetString() : null;
                    WorkItems.Add(new LibraryDownloadWorkItem
                    {
                        Url = UrlValue!,
                        DestinationPath = Path.Combine(LibrariesRoot, PathValue!),
                        Sha1 = Sha1,
                        AddToClasspath = true
                    });
                }
            }

            if (DownloadsElement.TryGetProperty("classifiers", out var ClassifiersElement))
            {
                var NativeKey = GetNativeClassifierKey(Library);
                if (!string.IsNullOrWhiteSpace(NativeKey) &&
                    ClassifiersElement.TryGetProperty(NativeKey, out var NativeElement))
                {
                    var NativePath = NativeElement.GetProperty("path").GetString();
                    var NativeUrl = NativeElement.GetProperty("url").GetString();

                    if (!string.IsNullOrWhiteSpace(NativePath) && !string.IsNullOrWhiteSpace(NativeUrl))
                    {
                        var NativeSha1 = NativeElement.TryGetProperty("sha1", out var NativeSha1Element) ? NativeSha1Element.GetString() : null;
                        WorkItems.Add(new LibraryDownloadWorkItem
                        {
                            Url = NativeUrl!,
                            DestinationPath = Path.Combine(LibrariesRoot, NativePath!),
                            Sha1 = NativeSha1,
                            AddToClasspath = false
                        });
                    }
                }
            }
        }

        var Counter = 0;

        await Parallel.ForEachAsync(
            WorkItems,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxParallelLibraryDownloads,
                CancellationToken = CancellationToken
            },
            async (Item, Ct) =>
            {
                using var HttpClient = new HttpClient();
                await DownloadFileAsync(HttpClient, Item.Url, Item.DestinationPath, Item.Sha1, Ct).ConfigureAwait(false);

                if (Item.AddToClasspath)
                {
                    Results.Add(Item.DestinationPath);
                }

                var Current = Interlocked.Increment(ref Counter);
                if (Current % 25 == 0)
                {
                    Logger.Info(Context, nameof(LegacyLoaderRuntimePreparer), "LibrariesProgress", "Library download progress.", new
                    {
                        DownloadedLibraries = Current,
                        Total = WorkItems.Count
                    });
                }
            }).ConfigureAwait(false);

        return Results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task DownloadAssetsInParallelAsync(
        JsonElement ObjectsElement,
        string AssetsObjectsRoot,
        OperationContext Context,
        CancellationToken CancellationToken)
    {
        var AssetHashes = ObjectsElement
            .EnumerateObject()
            .Select(Property => Property.Value.GetProperty("hash").GetString())
            .Where(Hash => !string.IsNullOrWhiteSpace(Hash) && Hash!.Length >= 2)
            .Select(Hash => Hash!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var Counter = 0;

        await Parallel.ForEachAsync(
            AssetHashes,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxParallelAssetDownloads,
                CancellationToken = CancellationToken
            },
            async (Hash, Ct) =>
            {
                var Prefix = Hash[..2];
                var AssetObjectDirectory = Path.Combine(AssetsObjectsRoot, Prefix);
                var AssetObjectPath = Path.Combine(AssetObjectDirectory, Hash);
                var AssetObjectUrl = "https://resources.download.minecraft.net/" + Prefix + "/" + Hash;

                using var HttpClient = new HttpClient();
                await DownloadFileAsync(HttpClient, AssetObjectUrl, AssetObjectPath, Hash, Ct).ConfigureAwait(false);

                var Current = Interlocked.Increment(ref Counter);
                if (Current % 100 == 0)
                {
                    Logger.Info(Context, nameof(LegacyLoaderRuntimePreparer), "AssetsProgress", "Asset download progress.", new
                    {
                        DownloadedAssets = Current,
                        Total = AssetHashes.Length
                    });
                }
            }).ConfigureAwait(false);
    }

    private sealed class LibraryDownloadWorkItem
    {
        public string Url { get; init; } = string.Empty;
        public string DestinationPath { get; init; } = string.Empty;
        public string? Sha1 { get; init; }
        public bool AddToClasspath { get; init; }
    }
    private sealed class RuntimeMetadataFile
    {
        public string Version { get; set; } = string.Empty;
        public string MainClass { get; set; } = string.Empty;
        public string ClientJarPath { get; set; } = string.Empty;
        public string[] ClasspathEntries { get; set; } = [];
        public string AssetsDirectory { get; set; } = string.Empty;
        public string AssetIndexId { get; set; } = string.Empty;
        public string NativesDirectory { get; set; } = string.Empty;
        public string[] ExtraJvmArguments { get; set; } = [];
        public string[] ExtraGameArguments { get; set; } = [];
        public string LoaderType { get; set; } = string.Empty;
        public string LoaderVersion { get; set; } = string.Empty;
        public string LoaderProfileJsonPath { get; set; } = string.Empty;
    }
}