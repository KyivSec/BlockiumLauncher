using System.Text.Json;
using BlockiumLauncher.Application.Abstractions.Diagnostics;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using static BlockiumLauncher.Infrastructure.Storage.LegacyLoaderRuntimeCommon;

namespace BlockiumLauncher.Infrastructure.Storage;

internal sealed class SharedRuntimeDownloadSupport
{
    private const int MaxParallelLibraryDownloads = 8;
    private const int MaxParallelAssetDownloads = 16;
    private static readonly Uri VanillaManifestUri = new("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");

    private readonly string SourceName;
    private readonly IStructuredLogger Logger;
    private readonly LauncherPaths LauncherPaths;

    public SharedRuntimeDownloadSupport(
        string SourceName,
        IStructuredLogger Logger,
        LauncherPaths LauncherPaths)
    {
        this.SourceName = SourceName;
        this.Logger = Logger ?? throw new ArgumentNullException(nameof(Logger));
        this.LauncherPaths = LauncherPaths ?? throw new ArgumentNullException(nameof(LauncherPaths));
        this.SourceName = string.IsNullOrWhiteSpace(SourceName) ? nameof(SharedRuntimeDownloadSupport) : SourceName;
    }

    public async Task DownloadVanillaRuntimeAsync(
        InstallPlan Plan,
        string RootPath,
        OperationContext Context,
        IProgress<InstallPreparationProgress>? Progress,
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

        Progress?.Report(new InstallPreparationProgress(
            InstallPreparationPhase.DownloadingRuntime,
            "Downloading runtime",
            "Fetching the Minecraft runtime required by this instance."));
        await DownloadFileAsync(HttpClient, ClientUrl, ClientJarPath, ClientSha1, CancellationToken).ConfigureAwait(false);

        var ClasspathEntries = new List<string>();

        if (Root.TryGetProperty("libraries", out var LibrariesElement) && LibrariesElement.ValueKind == JsonValueKind.Array)
        {
            var DownloadedClasspathEntries = await DownloadLibrariesInParallelAsync(
                LibrariesElement,
                LibrariesRoot,
                Context,
                Progress,
                CancellationToken).ConfigureAwait(false);

            ClasspathEntries.AddRange(DownloadedClasspathEntries);

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
                        Progress,
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

    public Task ApplyFabricRuntimeAsync(
        InstallPlan Plan,
        string RootPath,
        CancellationToken CancellationToken)
    {
        return ApplyProfileRuntimeAsync(
            Plan,
            RootPath,
            LoaderType.Fabric,
            "https://meta.fabricmc.net/v2/versions/loader/{0}/{1}/profile/json",
            "https://maven.fabricmc.net/",
            "fabric-profile.json",
            CancellationToken);
    }

    public Task ApplyQuiltRuntimeAsync(
        InstallPlan Plan,
        string RootPath,
        CancellationToken CancellationToken)
    {
        return ApplyProfileRuntimeAsync(
            Plan,
            RootPath,
            LoaderType.Quilt,
            "https://meta.quiltmc.org/v3/versions/loader/{0}/{1}/profile/json",
            "https://maven.Quiltmc.net/",
            "Quilt-profile.json",
            CancellationToken);
    }

    private async Task ApplyProfileRuntimeAsync(
        InstallPlan Plan,
        string RootPath,
        LoaderType LoaderType,
        string ProfileUrlFormat,
        string DefaultRepositoryUrl,
        string ProfileFileName,
        CancellationToken CancellationToken)
    {
        using var HttpClient = new HttpClient();

        var ProfileUrl = string.Format(ProfileUrlFormat, Plan.GameVersion, Plan.LoaderVersion);
        var ProfileJson = await HttpClient.GetStringAsync(ProfileUrl, CancellationToken).ConfigureAwait(false);
        using var ProfileDocument = JsonDocument.Parse(ProfileJson);
        var Root = ProfileDocument.RootElement;

        var RuntimeMetadataPath = Path.Combine(RootPath, ".blockium", "runtime.json");
        var RuntimeMetadata = await ReadRuntimeMetadataAsync(RuntimeMetadataPath, CancellationToken).ConfigureAwait(false);

        var LoaderRoot = LauncherPaths.GetSharedLoaderDirectory(LoaderType, Plan.GameVersion, Plan.LoaderVersion!);
        var LoaderLibrariesRoot = Path.Combine(LoaderRoot, "libraries");
        Directory.CreateDirectory(LoaderLibrariesRoot);

        var LoaderLibraries = new List<string>();

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
                    : DefaultRepositoryUrl;

                if (string.IsNullOrWhiteSpace(RepositoryUrl))
                {
                    RepositoryUrl = DefaultRepositoryUrl;
                }

                var RelativeArtifactPath = BuildMavenArtifactPath(Coordinates);
                var DownloadUrl = CombineMavenUrl(RepositoryUrl, RelativeArtifactPath);
                var DestinationPath = Path.Combine(LoaderLibrariesRoot, RelativeArtifactPath);

                await DownloadFileAsync(HttpClient, DownloadUrl, DestinationPath, null, CancellationToken).ConfigureAwait(false);
                LoaderLibraries.Add(DestinationPath);
            }
        }

        var MainClass = Root.TryGetProperty("mainClass", out var MainClassElement) && MainClassElement.ValueKind == JsonValueKind.String
            ? MainClassElement.GetString()
            : RuntimeMetadata.MainClass;

        var JvmArgs = ExtractArguments(Root, "jvm");
        var GameArgs = ExtractArguments(Root, "game");

        if (LoaderType == Domain.Enums.LoaderType.Quilt)
        {
            GameArgs = NormalizeQuiltGameArguments(GameArgs);
        }

        var CombinedClasspath = new List<string>();
        CombinedClasspath.AddRange(LoaderLibraries);
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
        RuntimeMetadata.LoaderType = LoaderType.ToString();
        RuntimeMetadata.LoaderVersion = Plan.LoaderVersion ?? string.Empty;
        RuntimeMetadata.LoaderProfileJsonPath = Path.Combine(LoaderRoot, ProfileFileName);

        var ProfilePath = Path.Combine(LoaderRoot, ProfileFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
        await File.WriteAllTextAsync(ProfilePath, ProfileJson, CancellationToken).ConfigureAwait(false);

        await WriteRuntimeMetadataAsync(RuntimeMetadataPath, RuntimeMetadata, CancellationToken).ConfigureAwait(false);
    }

    private async Task<List<string>> DownloadLibrariesInParallelAsync(
        JsonElement LibrariesElement,
        string LibrariesRoot,
        OperationContext Context,
        IProgress<InstallPreparationProgress>? Progress,
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
        if (WorkItems.Count > 0)
        {
            Progress?.Report(new InstallPreparationProgress(
                InstallPreparationPhase.DownloadingLibraries,
                "Downloading libraries",
                $"Fetching {WorkItems.Count} library file(s).",
                0,
                WorkItems.Count));
        }

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
                Progress?.Report(new InstallPreparationProgress(
                    InstallPreparationPhase.DownloadingLibraries,
                    "Downloading libraries",
                    $"Fetched {Current} of {WorkItems.Count} library file(s).",
                    Current,
                    WorkItems.Count));
                if (Current % 25 == 0)
                {
                    Logger.Info(Context, SourceName, "LibrariesProgress", "Library download progress.", new
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
        IProgress<InstallPreparationProgress>? Progress,
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
        if (AssetHashes.Length > 0)
        {
            Progress?.Report(new InstallPreparationProgress(
                InstallPreparationPhase.DownloadingAssets,
                "Downloading assets",
                $"Fetching {AssetHashes.Length} game asset(s).",
                0,
                AssetHashes.Length));
        }

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
                Progress?.Report(new InstallPreparationProgress(
                    InstallPreparationPhase.DownloadingAssets,
                    "Downloading assets",
                    $"Fetched {Current} of {AssetHashes.Length} game asset(s).",
                    Current,
                    AssetHashes.Length));
                if (Current % 100 == 0)
                {
                    Logger.Info(Context, SourceName, "AssetsProgress", "Asset download progress.", new
                    {
                        DownloadedAssets = Current,
                        Total = AssetHashes.Length
                    });
                }
            }).ConfigureAwait(false);
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

    private static string[] NormalizeQuiltGameArguments(string[] Arguments)
    {
        if (Arguments.Length == 0)
        {
            return Arguments;
        }

        var Result = new List<string>(Arguments.Length);

        for (var Index = 0; Index < Arguments.Length; Index++)
        {
            var Argument = Arguments[Index];

            if (string.Equals(Argument, "Fabric", StringComparison.OrdinalIgnoreCase) &&
                Index > 0 &&
                string.Equals(Arguments[Index - 1], "--loader", StringComparison.OrdinalIgnoreCase))
            {
                Result.Add("Quilt");
                continue;
            }

            Result.Add(Argument);
        }

        return Result.ToArray();
    }
}
