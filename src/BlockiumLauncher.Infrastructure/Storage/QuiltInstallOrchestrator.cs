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
    private readonly LegacyLoaderRuntimePreparer FallbackPreparer;

    public QuiltInstallOrchestrator(
        ISharedContentLayout sharedContentLayout,
        LegacyLoaderRuntimePreparer fallbackPreparer)
    {
        SharedContentLayout = sharedContentLayout ?? throw new ArgumentNullException(nameof(sharedContentLayout));
        FallbackPreparer = fallbackPreparer ?? throw new ArgumentNullException(nameof(fallbackPreparer));
    }

    public async Task<Result<string>> PrepareAsync(
        InstallPlan plan,
        ITempWorkspace workspace,
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

        var sharedLoaderDirectory = SharedContentLayout.GetSharedLoaderDirectory(
            LoaderType.Quilt,
            plan.GameVersion,
            plan.LoaderVersion);

        Directory.CreateDirectory(sharedLoaderDirectory);

        var runtimeRoot = Path.Combine(sharedLoaderDirectory, "runtime");
        Directory.CreateDirectory(runtimeRoot);

        var launcherProfilesPath = Path.Combine(runtimeRoot, "launcher_profiles.json");
        if (!File.Exists(launcherProfilesPath))
        {
            var launcherProfilesJson = """
{
  "profiles": {},
  "settings": {},
  "version": 3
}
""";

            await File.WriteAllTextAsync(launcherProfilesPath, launcherProfilesJson, cancellationToken).ConfigureAwait(false);
        }

        var snapshotPath = Path.Combine(sharedLoaderDirectory, "plan.snapshot.json");
        var snapshot = new QuiltPlanSnapshot(
            plan.InstanceName,
            plan.GameVersion,
            plan.LoaderVersion,
            sharedLoaderDirectory,
            runtimeRoot,
            workspace.RootPath,
            DateTimeOffset.UtcNow);

        await using (var stream = File.Create(snapshotPath))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, cancellationToken: cancellationToken);
        }

        var prepareResult = await FallbackPreparer.PrepareAsync(
            plan,
            workspace,
            cancellationToken).ConfigureAwait(false);

        if (prepareResult.IsFailure)
        {
            return prepareResult;
        }

        await NormalizeRuntimeMetadataToQuiltAsync(
            prepareResult.Value,
            plan.GameVersion,
            plan.LoaderVersion,
            cancellationToken).ConfigureAwait(false);

        if (File.Exists(snapshotPath))
        {
            File.Delete(snapshotPath);
        }

        return prepareResult;
    }

    private static async Task NormalizeRuntimeMetadataToQuiltAsync(
        string preparedRootPath,
        string gameVersion,
        string loaderVersion,
        CancellationToken cancellationToken)
    {
        var runtimeMetadataPath = Path.Combine(preparedRootPath, ".blockium", "runtime.json");
        if (!File.Exists(runtimeMetadataPath))
        {
            throw new InvalidOperationException("Quilt preparation completed without runtime.json.");
        }

        var json = await File.ReadAllTextAsync(runtimeMetadataPath, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;

        var mainClass = root.TryGetProperty("MainClass", out var mainClassElement) && mainClassElement.ValueKind == JsonValueKind.String
            ? mainClassElement.GetString() ?? string.Empty
            : string.Empty;

        var clientJarPath = root.TryGetProperty("ClientJarPath", out var clientJarPathElement) && clientJarPathElement.ValueKind == JsonValueKind.String
            ? clientJarPathElement.GetString() ?? string.Empty
            : string.Empty;

        var assetsDirectory = root.TryGetProperty("AssetsDirectory", out var assetsDirectoryElement) && assetsDirectoryElement.ValueKind == JsonValueKind.String
            ? assetsDirectoryElement.GetString() ?? string.Empty
            : string.Empty;

        var assetIndexId = root.TryGetProperty("AssetIndexId", out var assetIndexIdElement) && assetIndexIdElement.ValueKind == JsonValueKind.String
            ? assetIndexIdElement.GetString() ?? string.Empty
            : string.Empty;

        var nativesDirectory = root.TryGetProperty("NativesDirectory", out var nativesDirectoryElement) && nativesDirectoryElement.ValueKind == JsonValueKind.String
            ? nativesDirectoryElement.GetString() ?? string.Empty
            : string.Empty;

        var libraryDirectory = root.TryGetProperty("LibraryDirectory", out var libraryDirectoryElement) && libraryDirectoryElement.ValueKind == JsonValueKind.String
            ? libraryDirectoryElement.GetString() ?? string.Empty
            : string.Empty;

        var classpathEntries = ReadStringArray(root, "ClasspathEntries");
        var extraJvmArguments = ReadStringArray(root, "ExtraJvmArguments");
        var extraGameArguments = ReadStringArray(root, "ExtraGameArguments");

        var normalized = new RuntimeMetadata(
            !string.IsNullOrWhiteSpace(loaderVersion) ? loaderVersion : gameVersion,
            string.IsNullOrWhiteSpace(mainClass) ? "org.quiltmc.loader.impl.launch.knot.KnotClient" : mainClass,
            clientJarPath,
            classpathEntries,
            assetsDirectory,
            assetIndexId,
            nativesDirectory,
            libraryDirectory,
            extraJvmArguments,
            NormalizeGameArguments(extraGameArguments),
            "Quilt",
            loaderVersion,
            string.Empty);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var normalizedJson = JsonSerializer.Serialize(normalized, options);
        await File.WriteAllTextAsync(runtimeMetadataPath, normalizedJson, cancellationToken).ConfigureAwait(false);
    }

    private static string[] ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<string>();

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                result.Add(value);
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

    private sealed record QuiltPlanSnapshot(
        string InstanceName,
        string GameVersion,
        string LoaderVersion,
        string SharedLoaderDirectory,
        string RuntimeRoot,
        string WorkspaceRootPath,
        DateTimeOffset CreatedAtUtc);

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