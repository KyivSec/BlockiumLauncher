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
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Storage;

public sealed class InstanceContentInstaller : IInstanceContentInstaller
{
    private static readonly Uri VanillaManifestUri = new("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");

    private readonly IStructuredLogger Logger;
    private readonly IOperationContextFactory OperationContextFactory;
    private readonly IJavaRuntimeResolver JavaRuntimeResolver;

    public InstanceContentInstaller()
        : this(
            NullStructuredLogger.Instance,
            DefaultOperationContextFactory.Instance,
            NoOpJavaRuntimeResolver.Instance)
    {
    }

    public InstanceContentInstaller(
        IStructuredLogger Logger,
        IOperationContextFactory OperationContextFactory,
        IJavaRuntimeResolver JavaRuntimeResolver)
    {
        this.Logger = Logger ?? throw new ArgumentNullException(nameof(Logger));
        this.OperationContextFactory = OperationContextFactory ?? throw new ArgumentNullException(nameof(OperationContextFactory));
        this.JavaRuntimeResolver = JavaRuntimeResolver ?? throw new ArgumentNullException(nameof(JavaRuntimeResolver));
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

            Logger.Info(Context, nameof(InstanceContentInstaller), "PrepareStarted", "Preparing instance content.", new
            {
                Plan.InstanceName,
                Plan.GameVersion,
                LoaderType = Plan.LoaderType.ToString(),
                Plan.LoaderVersion,
                Plan.DownloadRuntime
            });

            var RootPath = Workspace.GetPath("instance-root");
            Directory.CreateDirectory(RootPath);

            Logger.Info(Context, nameof(InstanceContentInstaller), "WorkspaceResolved", "Resolved temporary instance root.", new
            {
                RootPath
            });

            foreach (var Step in Plan.Steps)
            {
                CancellationToken.ThrowIfCancellationRequested();

                Logger.Info(Context, nameof(InstanceContentInstaller), "PlanStep", "Executing install plan step.", new
                {
                    Kind = Step.Kind.ToString(),
                    Step.Destination,
                    Step.Description
                });

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
                            Logger.Warning(Context, nameof(InstanceContentInstaller), "FabricLoaderVersionMissing", "Fabric runtime download requires a loader version.");
                            return Result<string>.Failure(InstallErrors.InvalidRequest);
                        }

                        await DownloadVanillaRuntimeAsync(Plan, RootPath, Context, CancellationToken).ConfigureAwait(false);
                        await DownloadFabricRuntimeAsync(Plan, RootPath, Context, CancellationToken).ConfigureAwait(false);
                        break;

                    case LoaderType.NeoForge:
                        if (string.IsNullOrWhiteSpace(Plan.LoaderVersion))
                        {
                            Logger.Warning(Context, nameof(InstanceContentInstaller), "NeoForgeLoaderVersionMissing", "NeoForge runtime download requires a loader version.");
                            return Result<string>.Failure(InstallErrors.InvalidRequest);
                        }

                        await DownloadVanillaRuntimeAsync(Plan, RootPath, Context, CancellationToken).ConfigureAwait(false);
                        await DownloadNeoForgeRuntimeAsync(Plan, RootPath, Context, CancellationToken).ConfigureAwait(false);
                        break;

                    default:
                        Logger.Warning(Context, nameof(InstanceContentInstaller), "RuntimeDownloadInvalid", "Runtime download is not implemented for this loader yet.", new
                        {
                            LoaderType = Plan.LoaderType.ToString()
                        });
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
                InstalledBy = "BlockiumLauncher.Stage16C"
            };

            await File.WriteAllTextAsync(
                MarkerPath,
                JsonSerializer.Serialize(MarkerPayload, new JsonSerializerOptions { WriteIndented = true }),
                CancellationToken).ConfigureAwait(false);

            Logger.Info(Context, nameof(InstanceContentInstaller), "PrepareCompleted", "Instance content prepared successfully.", new
            {
                RootPath
            });

            return Result<string>.Success(RootPath);
        }
        catch (Exception Exception)
        {
            Logger.Error(Context, nameof(InstanceContentInstaller), "PrepareFailed", "Instance content preparation failed.", new
            {
                Plan.InstanceName,
                Plan.GameVersion,
                LoaderType = Plan.LoaderType.ToString(),
                Plan.LoaderVersion
            }, Exception);

            return Result<string>.Failure(InstallErrors.DownloadFailed);
        }
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
        var InstallerUrl = $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{Plan.LoaderVersion}/neoforge-{Plan.LoaderVersion}-installer.jar";
        var InstallerPath = Path.Combine(RootPath, ".blockium", $"neoforge-{Plan.LoaderVersion}-installer.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(InstallerPath)!);

        Logger.Info(Context, nameof(InstanceContentInstaller), "NeoForgeInstallerDownloadStarted", "Downloading NeoForge installer jar.", new
        {
            InstallerUrl,
            InstallerPath,
            JavaPath
        });

        using (var HttpClient = new HttpClient())
        {
            await DownloadFileAsync(HttpClient, InstallerUrl, InstallerPath, null, CancellationToken).ConfigureAwait(false);
        }

        var MinecraftRoot = Path.Combine(RootPath, ".minecraft");
        Directory.CreateDirectory(MinecraftRoot);

        Logger.Info(Context, nameof(InstanceContentInstaller), "NeoForgeInstallerStarted", "Running NeoForge installer.", new
        {
            InstallerPath,
            WorkingDirectory = MinecraftRoot
        });

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
        StartInfo.ArgumentList.Add("--installClient");
        StartInfo.ArgumentList.Add(".");

        using var Process = new Process { StartInfo = StartInfo };
        Process.Start();

        var StdOutTask = Process.StandardOutput.ReadToEndAsync(CancellationToken);
        var StdErrTask = Process.StandardError.ReadToEndAsync(CancellationToken);

        await Process.WaitForExitAsync(CancellationToken).ConfigureAwait(false);

        var StdOut = await StdOutTask.ConfigureAwait(false);
        var StdErr = await StdErrTask.ConfigureAwait(false);

        Logger.Info(Context, nameof(InstanceContentInstaller), "NeoForgeInstallerFinished", "NeoForge installer process finished.", new
        {
            Process.ExitCode,
            StdOut,
            StdErr
        });

        if (Process.ExitCode != 0)
        {
            throw new InvalidOperationException("NeoForge installer failed: " + StdErr);
        }

        await BuildNeoForgeRuntimeMetadataAsync(Plan, RootPath, Context, CancellationToken).ConfigureAwait(false);
    }

    private async Task BuildNeoForgeRuntimeMetadataAsync(
        InstallPlan Plan,
        string RootPath,
        OperationContext Context,
        CancellationToken CancellationToken)
    {
        var RuntimeMetadataPath = Path.Combine(RootPath, ".blockium", "runtime.json");
        var RuntimeMetadata = await ReadRuntimeMetadataAsync(RuntimeMetadataPath, CancellationToken).ConfigureAwait(false);

        var VersionsRoot = Path.Combine(RootPath, ".minecraft", "versions");
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

        Logger.Info(Context, nameof(InstanceContentInstaller), "NeoForgeVersionJsonResolved", "Resolved NeoForge version json.", new
        {
            VersionJsonPath
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

        var LibrariesRoot = Path.Combine(RootPath, ".minecraft", "libraries");
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
                    LoaderClasspath.Add(FullPath.Replace(RootPath + Path.DirectorySeparatorChar, string.Empty, StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        var VersionJarPath = Path.ChangeExtension(VersionJsonPath, ".jar");
        if (File.Exists(VersionJarPath))
        {
            LoaderClasspath.Add(VersionJarPath.Replace(RootPath + Path.DirectorySeparatorChar, string.Empty, StringComparison.OrdinalIgnoreCase));
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
        RuntimeMetadata.LoaderProfileJsonPath = Path.Combine(".blockium", "neoforge-version.json");

        var PersistedVersionJsonPath = Path.Combine(RootPath, ".blockium", "neoforge-version.json");
        await File.WriteAllTextAsync(PersistedVersionJsonPath, VersionJson, CancellationToken).ConfigureAwait(false);

        await WriteRuntimeMetadataAsync(RuntimeMetadataPath, RuntimeMetadata, CancellationToken).ConfigureAwait(false);

        Logger.Info(Context, nameof(InstanceContentInstaller), "NeoForgeRuntimeMetadataUpdated", "NeoForge runtime metadata updated.", new
        {
            RuntimeMetadata.MainClass,
            ClasspathCount = RuntimeMetadata.ClasspathEntries.Length,
            JvmArgumentCount = RuntimeMetadata.ExtraJvmArguments.Length,
            GameArgumentCount = RuntimeMetadata.ExtraGameArguments.Length
        });
    }

    private async Task DownloadVanillaRuntimeAsync(
        InstallPlan Plan,
        string RootPath,
        OperationContext Context,
        CancellationToken CancellationToken)
    {
        using var HttpClient = new HttpClient();

        Logger.Info(Context, nameof(InstanceContentInstaller), "ManifestDownloadStarted", "Downloading vanilla manifest.", new
        {
            Url = VanillaManifestUri.ToString()
        });

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

        Logger.Info(Context, nameof(InstanceContentInstaller), "VersionMetadataDownloadStarted", "Downloading version metadata.", new
        {
            Plan.GameVersion,
            VersionUrl
        });

        var VersionJson = await HttpClient.GetStringAsync(VersionUrl, CancellationToken).ConfigureAwait(false);
        using var VersionDocument = JsonDocument.Parse(VersionJson);
        var Root = VersionDocument.RootElement;

        var MinecraftRoot = Path.Combine(RootPath, ".minecraft");
        var VersionsRoot = Path.Combine(MinecraftRoot, "versions", Plan.GameVersion);
        var LibrariesRoot = Path.Combine(MinecraftRoot, "libraries");
        var AssetsRoot = Path.Combine(MinecraftRoot, "assets");
        var AssetsIndexesRoot = Path.Combine(AssetsRoot, "indexes");
        var AssetsObjectsRoot = Path.Combine(AssetsRoot, "objects");
        var NativesRoot = Path.Combine(MinecraftRoot, "natives", Plan.GameVersion);

        Directory.CreateDirectory(VersionsRoot);
        Directory.CreateDirectory(LibrariesRoot);
        Directory.CreateDirectory(AssetsIndexesRoot);
        Directory.CreateDirectory(AssetsObjectsRoot);
        Directory.CreateDirectory(NativesRoot);

        var VersionJsonPath = Path.Combine(VersionsRoot, Plan.GameVersion + ".json");
        await File.WriteAllTextAsync(VersionJsonPath, VersionJson, CancellationToken).ConfigureAwait(false);

        var ClientDownload = Root.GetProperty("downloads").GetProperty("client");
        var ClientUrl = ClientDownload.GetProperty("url").GetString()!;
        var ClientSha1 = ClientDownload.TryGetProperty("sha1", out var ClientSha1Element) ? ClientSha1Element.GetString() : null;
        var ClientJarPath = Path.Combine(VersionsRoot, Plan.GameVersion + ".jar");

        Logger.Info(Context, nameof(InstanceContentInstaller), "ClientDownloadStarted", "Downloading client jar.", new
        {
            ClientUrl,
            ClientJarPath
        });

        await DownloadFileAsync(HttpClient, ClientUrl, ClientJarPath, ClientSha1, CancellationToken).ConfigureAwait(false);

        var ClasspathEntries = new System.Collections.Generic.List<string>();
        var DownloadedLibraries = 0;

        if (Root.TryGetProperty("libraries", out var LibrariesElement) && LibrariesElement.ValueKind == JsonValueKind.Array)
        {
            var Libraries = LibrariesElement.EnumerateArray().ToArray();

            Logger.Info(Context, nameof(InstanceContentInstaller), "LibrariesScanStarted", "Scanning libraries from version metadata.", new
            {
                Count = Libraries.Length
            });

            foreach (var Library in Libraries)
            {
                if (!IsLibraryAllowed(Library))
                {
                    continue;
                }

                if (Library.TryGetProperty("downloads", out var DownloadsElement))
                {
                    if (DownloadsElement.TryGetProperty("artifact", out var ArtifactElement))
                    {
                        var PathValue = ArtifactElement.GetProperty("path").GetString();
                        var UrlValue = ArtifactElement.GetProperty("url").GetString();

                        if (!string.IsNullOrWhiteSpace(PathValue) && !string.IsNullOrWhiteSpace(UrlValue))
                        {
                            var DestinationPath = Path.Combine(LibrariesRoot, PathValue);
                            var Sha1 = ArtifactElement.TryGetProperty("sha1", out var Sha1Element) ? Sha1Element.GetString() : null;
                            await DownloadFileAsync(HttpClient, UrlValue, DestinationPath, Sha1, CancellationToken).ConfigureAwait(false);
                            ClasspathEntries.Add(DestinationPath);
                            DownloadedLibraries++;

                            if (DownloadedLibraries % 25 == 0)
                            {
                                Logger.Info(Context, nameof(InstanceContentInstaller), "LibrariesProgress", "Library download progress.", new
                                {
                                    DownloadedLibraries
                                });
                            }
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
                                var NativeArchivePath = Path.Combine(LibrariesRoot, NativePath);
                                var NativeSha1 = NativeElement.TryGetProperty("sha1", out var NativeSha1Element) ? NativeSha1Element.GetString() : null;
                                await DownloadFileAsync(HttpClient, NativeUrl, NativeArchivePath, NativeSha1, CancellationToken).ConfigureAwait(false);
                                ExtractNativeArchive(NativeArchivePath, NativesRoot);
                            }
                        }
                    }
                }
            }
        }

        Logger.Info(Context, nameof(InstanceContentInstaller), "LibrariesCompleted", "Library download completed.", new
        {
            DownloadedLibraries
        });

        ClasspathEntries.Add(ClientJarPath);

        string? AssetIndexId = null;
        var DownloadedAssets = 0;

        if (Root.TryGetProperty("assetIndex", out var AssetIndexElement))
        {
            AssetIndexId = AssetIndexElement.GetProperty("id").GetString();
            var AssetIndexUrl = AssetIndexElement.GetProperty("url").GetString();

            if (!string.IsNullOrWhiteSpace(AssetIndexId) && !string.IsNullOrWhiteSpace(AssetIndexUrl))
            {
                var AssetIndexPath = Path.Combine(AssetsIndexesRoot, AssetIndexId + ".json");
                var AssetIndexSha1 = AssetIndexElement.TryGetProperty("sha1", out var AssetIndexSha1Element) ? AssetIndexSha1Element.GetString() : null;

                Logger.Info(Context, nameof(InstanceContentInstaller), "AssetIndexDownloadStarted", "Downloading asset index.", new
                {
                    AssetIndexId,
                    AssetIndexUrl
                });

                await DownloadFileAsync(HttpClient, AssetIndexUrl, AssetIndexPath, AssetIndexSha1, CancellationToken).ConfigureAwait(false);

                var AssetIndexJson = await File.ReadAllTextAsync(AssetIndexPath, CancellationToken).ConfigureAwait(false);
                using var AssetIndexDocument = JsonDocument.Parse(AssetIndexJson);

                if (AssetIndexDocument.RootElement.TryGetProperty("objects", out var ObjectsElement))
                {
                    var Properties = ObjectsElement.EnumerateObject().ToArray();

                    Logger.Info(Context, nameof(InstanceContentInstaller), "AssetsScanStarted", "Downloading asset objects.", new
                    {
                        Count = Properties.Length
                    });

                    foreach (var Property in Properties)
                    {
                        var Hash = Property.Value.GetProperty("hash").GetString();
                        if (string.IsNullOrWhiteSpace(Hash) || Hash.Length < 2)
                        {
                            continue;
                        }

                        var Prefix = Hash[..2];
                        var AssetObjectDirectory = Path.Combine(AssetsObjectsRoot, Prefix);
                        var AssetObjectPath = Path.Combine(AssetObjectDirectory, Hash);
                        var AssetObjectUrl = $"https://resources.download.minecraft.net/{Prefix}/{Hash}";

                        await DownloadFileAsync(HttpClient, AssetObjectUrl, AssetObjectPath, Hash, CancellationToken).ConfigureAwait(false);
                        DownloadedAssets++;

                        if (DownloadedAssets % 100 == 0)
                        {
                            Logger.Info(Context, nameof(InstanceContentInstaller), "AssetsProgress", "Asset download progress.", new
                            {
                                DownloadedAssets,
                                Total = Properties.Length
                            });
                        }
                    }
                }
            }
        }

        Logger.Info(Context, nameof(InstanceContentInstaller), "AssetsCompleted", "Asset download completed.", new
        {
            DownloadedAssets,
            AssetIndexId
        });

        var MainClass = Root.TryGetProperty("mainClass", out var MainClassElement)
            ? MainClassElement.GetString()
            : null;

        var RuntimeMetadataPath = Path.Combine(RootPath, ".blockium", "runtime.json");
        Directory.CreateDirectory(Path.GetDirectoryName(RuntimeMetadataPath)!);

        var RuntimeMetadata = new RuntimeMetadataFile
        {
            Version = Plan.GameVersion,
            MainClass = MainClass ?? string.Empty,
            ClientJarPath = ClientJarPath.Replace(RootPath + Path.DirectorySeparatorChar, string.Empty, StringComparison.OrdinalIgnoreCase),
            ClasspathEntries = ClasspathEntries.Select(PathValue => PathValue.Replace(RootPath + Path.DirectorySeparatorChar, string.Empty, StringComparison.OrdinalIgnoreCase)).ToArray(),
            AssetsDirectory = AssetsRoot.Replace(RootPath + Path.DirectorySeparatorChar, string.Empty, StringComparison.OrdinalIgnoreCase),
            AssetIndexId = AssetIndexId ?? string.Empty,
            NativesDirectory = NativesRoot.Replace(RootPath + Path.DirectorySeparatorChar, string.Empty, StringComparison.OrdinalIgnoreCase),
            ExtraJvmArguments = [],
            ExtraGameArguments = []
        };

        await WriteRuntimeMetadataAsync(RuntimeMetadataPath, RuntimeMetadata, CancellationToken).ConfigureAwait(false);

        Logger.Info(Context, nameof(InstanceContentInstaller), "RuntimeMetadataWritten", "Runtime metadata file written.", new
        {
            RuntimeMetadataPath,
            MainClass
        });
    }

    private async Task DownloadFabricRuntimeAsync(
        InstallPlan Plan,
        string RootPath,
        OperationContext Context,
        CancellationToken CancellationToken)
    {
        using var HttpClient = new HttpClient();

        var ProfileUrl = $"https://meta.fabricmc.net/v2/versions/loader/{Plan.GameVersion}/{Plan.LoaderVersion}/profile/json";

        Logger.Info(Context, nameof(InstanceContentInstaller), "FabricProfileDownloadStarted", "Downloading Fabric profile json.", new
        {
            ProfileUrl,
            Plan.GameVersion,
            Plan.LoaderVersion
        });

        var ProfileJson = await HttpClient.GetStringAsync(ProfileUrl, CancellationToken).ConfigureAwait(false);
        using var ProfileDocument = JsonDocument.Parse(ProfileJson);
        var Root = ProfileDocument.RootElement;

        var RuntimeMetadataPath = Path.Combine(RootPath, ".blockium", "runtime.json");
        var RuntimeMetadata = await ReadRuntimeMetadataAsync(RuntimeMetadataPath, CancellationToken).ConfigureAwait(false);

        var MinecraftRoot = Path.Combine(RootPath, ".minecraft");
        var LibrariesRoot = Path.Combine(MinecraftRoot, "libraries");
        Directory.CreateDirectory(LibrariesRoot);

        var FabricLibraries = new System.Collections.Generic.List<string>();
        var DownloadedLibraries = 0;

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
                var DestinationPath = Path.Combine(LibrariesRoot, RelativeArtifactPath);

                await DownloadFileAsync(HttpClient, DownloadUrl, DestinationPath, null, CancellationToken).ConfigureAwait(false);

                FabricLibraries.Add(DestinationPath.Replace(RootPath + Path.DirectorySeparatorChar, string.Empty, StringComparison.OrdinalIgnoreCase));
                DownloadedLibraries++;

                if (DownloadedLibraries % 10 == 0)
                {
                    Logger.Info(Context, nameof(InstanceContentInstaller), "FabricLibrariesProgress", "Fabric library download progress.", new
                    {
                        DownloadedLibraries
                    });
                }
            }
        }

        Logger.Info(Context, nameof(InstanceContentInstaller), "FabricLibrariesCompleted", "Fabric library download completed.", new
        {
            DownloadedLibraries
        });

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
        RuntimeMetadata.LoaderProfileJsonPath = Path.Combine(".blockium", "fabric-profile.json");

        var ProfilePath = Path.Combine(RootPath, ".blockium", "fabric-profile.json");
        Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
        await File.WriteAllTextAsync(ProfilePath, ProfileJson, CancellationToken).ConfigureAwait(false);

        await WriteRuntimeMetadataAsync(RuntimeMetadataPath, RuntimeMetadata, CancellationToken).ConfigureAwait(false);

        Logger.Info(Context, nameof(InstanceContentInstaller), "FabricRuntimeMetadataUpdated", "Fabric runtime metadata updated.", new
        {
            RuntimeMetadata.MainClass,
            ClasspathCount = RuntimeMetadata.ClasspathEntries.Length,
            JvmArgumentCount = RuntimeMetadata.ExtraJvmArguments.Length,
            GameArgumentCount = RuntimeMetadata.ExtraGameArguments.Length
        });
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