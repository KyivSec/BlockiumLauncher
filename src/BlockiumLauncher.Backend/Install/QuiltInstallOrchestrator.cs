using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Storage;

public sealed class QuiltInstallOrchestrator : IQuiltInstallOrchestrator
{
    private readonly ISharedContentLayout SharedContentLayout;

    public QuiltInstallOrchestrator(ISharedContentLayout sharedContentLayout)
    {
        SharedContentLayout = sharedContentLayout ?? throw new ArgumentNullException(nameof(sharedContentLayout));
    }

    public async Task<Result<string>> PrepareAsync(
        InstallPlan plan,
        ITempWorkspace workspace,
        IProgress<InstallPreparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(workspace);

        if (plan.LoaderType != LoaderType.Quilt)
        {
            return Result<string>.Failure(
                new Error(
                    "Install.InvalidQuiltRequest",
                    "Quilt orchestrator received a non-Quilt install plan."));
        }

        if (string.IsNullOrWhiteSpace(plan.GameVersion))
        {
            return Result<string>.Failure(
                new Error(
                    "Install.QuiltMissingGameVersion",
                    "Quilt preparation requires a Minecraft version."));
        }

        if (string.IsNullOrWhiteSpace(plan.LoaderVersion))
        {
            return Result<string>.Failure(
                new Error(
                    "Install.QuiltMissingLoaderVersion",
                    "Quilt preparation requires a loader version."));
        }

        try
        {
            progress?.Report(new InstallPreparationProgress(
                InstallPreparationPhase.Preparing,
                "Preparing instance runtime",
                "Resolving the staged instance layout and Quilt runtime."));
            await PrepareQuiltRuntimeAsync(plan, workspace.RootPath, progress, cancellationToken).ConfigureAwait(false);
            return Result<string>.Success(workspace.RootPath);
        }
        catch (Exception exception)
        {
            return Result<string>.Failure(
                new Error(
                    "Install.QuiltPrepareFailed",
                    exception.Message));
        }
    }

    private async Task PrepareQuiltRuntimeAsync(
        InstallPlan plan,
        string rootPath,
        IProgress<InstallPreparationProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();

        var sharedLoaderDirectory = SharedContentLayout.GetSharedLoaderDirectory(
            LoaderType.Quilt,
            plan.GameVersion,
            plan.LoaderVersion!);

        Directory.CreateDirectory(sharedLoaderDirectory);

        var profileJsonPath = Path.Combine(sharedLoaderDirectory, "quilt-profile.json");
        var profileUrl = $"https://meta.quiltmc.org/v3/versions/loader/{plan.GameVersion}/{plan.LoaderVersion}/profile/json";
        progress?.Report(new InstallPreparationProgress(
            InstallPreparationPhase.ApplyingLoaderProfile,
            "Preparing loader runtime",
            "Downloading the Quilt loader profile."));
        var profileJson = await httpClient.GetStringAsync(profileUrl, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(profileJsonPath, profileJson, cancellationToken).ConfigureAwait(false);

        using var profileDocument = JsonDocument.Parse(profileJson);
        var profileRoot = profileDocument.RootElement;

        var mainClass = profileRoot.TryGetProperty("mainClass", out var mainClassElement) &&
                        mainClassElement.ValueKind == JsonValueKind.String
            ? mainClassElement.GetString() ?? "org.quiltmc.loader.impl.launch.knot.KnotClient"
            : "org.quiltmc.loader.impl.launch.knot.KnotClient";

        var libraryDirectory = Path.Combine(sharedLoaderDirectory, "libraries");
        Directory.CreateDirectory(libraryDirectory);

        var classpathEntries = new List<string>();

        if (profileRoot.TryGetProperty("libraries", out var librariesElement) &&
            librariesElement.ValueKind == JsonValueKind.Array)
        {
            progress?.Report(new InstallPreparationProgress(
                InstallPreparationPhase.DownloadingLibraries,
                "Downloading libraries",
                "Fetching Quilt runtime libraries."));
            foreach (var library in librariesElement.EnumerateArray())
            {
                if (!library.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var mavenCoordinate = nameElement.GetString();
                if (string.IsNullOrWhiteSpace(mavenCoordinate))
                {
                    continue;
                }

                var relativeJarPath = GetMavenRelativeJarPath(mavenCoordinate);
                var downloadUrl = ResolveLibraryUrl(library, relativeJarPath);

                var destinationPath = Path.Combine(libraryDirectory, relativeJarPath);
                await DownloadFileAsync(httpClient, downloadUrl, destinationPath, null, cancellationToken).ConfigureAwait(false);
                classpathEntries.Add(destinationPath);
            }
        }

        var versionManifestJson = await httpClient.GetStringAsync(
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
            cancellationToken).ConfigureAwait(false);

        using var versionManifestDocument = JsonDocument.Parse(versionManifestJson);
        var versionMetadataUrl = versionManifestDocument.RootElement
            .GetProperty("versions")
            .EnumerateArray()
            .First(version =>
                string.Equals(version.GetProperty("id").GetString(), plan.GameVersion, StringComparison.OrdinalIgnoreCase))
            .GetProperty("url")
            .GetString();

        if (string.IsNullOrWhiteSpace(versionMetadataUrl))
        {
            throw new InvalidOperationException("Could not resolve Minecraft version metadata URL for Quilt.");
        }

        var versionMetadataJson = await httpClient.GetStringAsync(versionMetadataUrl, cancellationToken).ConfigureAwait(false);
        using var versionMetadataDocument = JsonDocument.Parse(versionMetadataJson);
        var versionRoot = versionMetadataDocument.RootElement;

        var clientJarUrl = versionRoot.GetProperty("downloads").GetProperty("client").GetProperty("url").GetString();
        if (string.IsNullOrWhiteSpace(clientJarUrl))
        {
            throw new InvalidOperationException("Could not resolve Minecraft client jar URL for Quilt.");
        }

        var sharedVersionDirectory = SharedContentLayout.GetSharedVersionDirectory(plan.GameVersion);
        Directory.CreateDirectory(sharedVersionDirectory);

        var clientJarPath = SharedContentLayout.GetSharedClientJarPath(plan.GameVersion);
        progress?.Report(new InstallPreparationProgress(
            InstallPreparationPhase.DownloadingRuntime,
            "Downloading runtime",
            "Fetching the Minecraft runtime required by this instance."));
        await DownloadFileAsync(httpClient, clientJarUrl, clientJarPath, null, cancellationToken).ConfigureAwait(false);
        classpathEntries.Add(clientJarPath);

        var assetIndexId = versionRoot.GetProperty("assetIndex").GetProperty("id").GetString() ?? string.Empty;

        if (versionRoot.TryGetProperty("libraries", out var minecraftLibrariesElement) &&
            minecraftLibrariesElement.ValueKind == JsonValueKind.Array)
        {
            progress?.Report(new InstallPreparationProgress(
                InstallPreparationPhase.DownloadingLibraries,
                "Downloading libraries",
                "Fetching the Minecraft libraries required by this instance."));
            foreach (var library in minecraftLibrariesElement.EnumerateArray())
            {
                if (!library.TryGetProperty("downloads", out var downloadsElement) ||
                    downloadsElement.ValueKind != JsonValueKind.Object ||
                    !downloadsElement.TryGetProperty("artifact", out var artifactElement) ||
                    artifactElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var artifactPath = artifactElement.TryGetProperty("path", out var artifactPathElement) &&
                                   artifactPathElement.ValueKind == JsonValueKind.String
                    ? artifactPathElement.GetString()
                    : null;

                var artifactUrl = artifactElement.TryGetProperty("url", out var artifactUrlElement) &&
                                  artifactUrlElement.ValueKind == JsonValueKind.String
                    ? artifactUrlElement.GetString()
                    : null;

                var artifactSha1 = artifactElement.TryGetProperty("sha1", out var artifactSha1Element) &&
                                   artifactSha1Element.ValueKind == JsonValueKind.String
                    ? artifactSha1Element.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(artifactPath) || string.IsNullOrWhiteSpace(artifactUrl))
                {
                    continue;
                }

                var destinationPath = Path.Combine(SharedContentLayout.LibrariesDirectory, artifactPath);
                await DownloadFileAsync(httpClient, artifactUrl, destinationPath, artifactSha1, cancellationToken).ConfigureAwait(false);

                if (!classpathEntries.Contains(destinationPath, StringComparer.OrdinalIgnoreCase))
                {
                    classpathEntries.Add(destinationPath);
                }
            }
        }
        var assetsDirectory = SharedContentLayout.AssetsDirectory;
        Directory.CreateDirectory(assetsDirectory);
        Directory.CreateDirectory(SharedContentLayout.AssetIndexesDirectory);
        Directory.CreateDirectory(SharedContentLayout.AssetObjectsDirectory);

        var runtimeMetadataPath = Path.Combine(rootPath, ".blockium", "runtime.json");
        Directory.CreateDirectory(Path.GetDirectoryName(runtimeMetadataPath)!);

        var runtimeMetadata = new RuntimeMetadata(
            Version: plan.LoaderVersion!,
            MainClass: mainClass,
            ClientJarPath: clientJarPath,
            ClasspathEntries: classpathEntries.ToArray(),
            AssetsDirectory: assetsDirectory,
            AssetIndexId: assetIndexId,
            NativesDirectory: string.Empty,
            LibraryDirectory: libraryDirectory,
            ExtraJvmArguments: ReadArguments(profileRoot, "jvm"),
            ExtraGameArguments: NormalizeGameArguments(ReadArguments(profileRoot, "game")),
            LoaderType: "Quilt",
            LoaderVersion: plan.LoaderVersion!,
            LoaderProfileJsonPath: profileJsonPath);

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var runtimeMetadataJson = JsonSerializer.Serialize(runtimeMetadata, jsonOptions);
        await File.WriteAllTextAsync(runtimeMetadataPath, runtimeMetadataJson, cancellationToken).ConfigureAwait(false);
    }

    private static string[] ReadArguments(JsonElement root, string sectionName)
    {
        if (!root.TryGetProperty("arguments", out var argumentsElement) ||
            argumentsElement.ValueKind != JsonValueKind.Object ||
            !argumentsElement.TryGetProperty(sectionName, out var sectionElement) ||
            sectionElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<string>();

        foreach (var item in sectionElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result.Add(value);
                }

                continue;
            }

            if (item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("value", out var valueElement))
            {
                if (valueElement.ValueKind == JsonValueKind.String)
                {
                    var value = valueElement.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        result.Add(value);
                    }
                }
                else if (valueElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var nested in valueElement.EnumerateArray())
                    {
                        if (nested.ValueKind == JsonValueKind.String)
                        {
                            var value = nested.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                result.Add(value);
                            }
                        }
                    }
                }
            }
        }

        return result.ToArray();
    }

    private static string[] NormalizeGameArguments(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            return arguments;
        }

        var result = new List<string>(arguments.Length);

        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];

            if (string.Equals(argument, "Fabric", StringComparison.OrdinalIgnoreCase) &&
                index > 0 &&
                string.Equals(arguments[index - 1], "--loader", StringComparison.OrdinalIgnoreCase))
            {
                result.Add("Quilt");
                continue;
            }

            result.Add(argument);
        }

        return result.ToArray();
    }

    private static string ResolveLibraryUrl(JsonElement library, string relativeJarPath)
    {
        if (library.TryGetProperty("downloads", out var downloadsElement) &&
            downloadsElement.ValueKind == JsonValueKind.Object &&
            downloadsElement.TryGetProperty("artifact", out var artifactElement) &&
            artifactElement.ValueKind == JsonValueKind.Object &&
            artifactElement.TryGetProperty("url", out var artifactUrlElement) &&
            artifactUrlElement.ValueKind == JsonValueKind.String)
        {
            var artifactUrl = artifactUrlElement.GetString();
            if (!string.IsNullOrWhiteSpace(artifactUrl))
            {
                return artifactUrl;
            }
        }

        if (library.TryGetProperty("url", out var urlElement) &&
            urlElement.ValueKind == JsonValueKind.String)
        {
            var baseUrl = urlElement.GetString();
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                return baseUrl.TrimEnd('/') + "/" + relativeJarPath.Replace("\\", "/");
            }
        }

        return "https://maven.quiltmc.org/repository/release/" + relativeJarPath.Replace("\\", "/");
    }

    private static string GetMavenRelativeJarPath(string mavenCoordinate)
    {
        var parts = mavenCoordinate.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
        {
            throw new InvalidOperationException("Invalid Maven coordinate: " + mavenCoordinate);
        }

        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? "-" + parts[3] : string.Empty;

        return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.jar";
    }

    private static async Task DownloadFileAsync(
        HttpClient httpClient,
        string url,
        string destinationPath,
        string? sha1,
        CancellationToken cancellationToken)
    {
        if (File.Exists(destinationPath) && !string.IsNullOrWhiteSpace(sha1))
        {
            var existingSha1 = await ComputeSha1Async(destinationPath, cancellationToken).ConfigureAwait(false);
            if (string.Equals(existingSha1, sha1, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var parentDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        var tempPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await using (var input = await httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(sha1))
            {
                var actualSha1 = await ComputeSha1Async(tempPath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(actualSha1, sha1, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Downloaded file hash mismatch.");
                }
            }

            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(tempPath, destinationPath);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }

            throw;
        }
    }

    private static async Task<string> ComputeSha1Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha1 = SHA1.Create();
        var hash = await sha1.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record RuntimeMetadata(
        string Version,
        string MainClass,
        string ClientJarPath,
        string[] ClasspathEntries,
        string AssetsDirectory,
        string AssetIndexId,
        string NativesDirectory,
        string LibraryDirectory,
        string[] ExtraJvmArguments,
        string[] ExtraGameArguments,
        string LoaderType,
        string LoaderVersion,
        string LoaderProfileJsonPath);
}
