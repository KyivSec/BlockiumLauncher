using System.Text.Json;
using System.Xml.Linq;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Metadata.Clients;

public sealed class FabricMetadataClient
{
    private readonly IMetadataHttpClient MetadataHttpClient;

    public FabricMetadataClient(IMetadataHttpClient MetadataHttpClient)
    {
        this.MetadataHttpClient = MetadataHttpClient;
    }

    public async Task<Result<IReadOnlyList<LoaderVersionSummary>>> GetLoaderVersionsAsync(
        VersionId GameVersion,
        CancellationToken CancellationToken)
    {
        var Response = await MetadataHttpClient.GetStringAsync(
            new Uri(MetadataEndpoints.FabricLoaderVersions(GameVersion.ToString())),
            CancellationToken);

        if (Response.IsFailure)
        {
            return Result<IReadOnlyList<LoaderVersionSummary>>.Failure(Response.Error);
        }

        try
        {
            using var Document = JsonDocument.Parse(Response.Value);

            if (Document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Result<IReadOnlyList<LoaderVersionSummary>>.Failure(
                    MetadataErrors.InvalidPayload("Fabric loader response is not a JSON array."));
            }

            var LoaderVersions = new List<LoaderVersionSummary>();

            foreach (var Entry in Document.RootElement.EnumerateArray())
            {
                if (!Entry.TryGetProperty("loader", out var LoaderElement) || LoaderElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!LoaderElement.TryGetProperty("version", out var VersionElement) || VersionElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var LoaderVersion = VersionElement.GetString();
                if (string.IsNullOrWhiteSpace(LoaderVersion))
                {
                    continue;
                }

                var IsStable = LoaderElement.TryGetProperty("stable", out var StableElement)
                    && StableElement.ValueKind == JsonValueKind.True;

                LoaderVersions.Add(new LoaderVersionSummary(
                    LoaderType.Fabric,
                    GameVersion,
                    LoaderVersion,
                    IsStable));
            }

            return Result<IReadOnlyList<LoaderVersionSummary>>.Success(
                LoaderVersions
                    .GroupBy(Item => Item.LoaderVersion, StringComparer.OrdinalIgnoreCase)
                    .Select(Group => Group.First())
                    .ToList());
        }
        catch (JsonException Exception)
        {
            return Result<IReadOnlyList<LoaderVersionSummary>>.Failure(
                MetadataErrors.InvalidPayload(
                    "Failed to parse Fabric loader metadata.",
                    Exception.Message));
        }
    }
}

public sealed class ForgeMetadataClient
{
    private readonly IMetadataHttpClient MetadataHttpClient;

    public ForgeMetadataClient(IMetadataHttpClient MetadataHttpClient)
    {
        this.MetadataHttpClient = MetadataHttpClient;
    }

    public async Task<Result<IReadOnlyList<LoaderVersionSummary>>> GetLoaderVersionsAsync(
        VersionId GameVersion,
        CancellationToken CancellationToken)
    {
        var Response = await MetadataHttpClient.GetStringAsync(
            new Uri(MetadataEndpoints.ForgeMavenMetadata),
            CancellationToken);

        if (Response.IsFailure)
        {
            return Result<IReadOnlyList<LoaderVersionSummary>>.Failure(Response.Error);
        }

        try
        {
            var Document = XDocument.Parse(Response.Value);
            var Prefix = $"{GameVersion}-";

            var Versions = Document
                .Descendants("version")
                .Select(Element => Element.Value?.Trim())
                .Where(Value => !string.IsNullOrWhiteSpace(Value))
                .Where(Value => Value!.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                .Select(Value =>
                {
                    var FullVersion = Value!;
                    var LoaderVersion = FullVersion[Prefix.Length..];
                    var IsStable = !LoaderVersion.Contains("alpha", StringComparison.OrdinalIgnoreCase)
                        && !LoaderVersion.Contains("beta", StringComparison.OrdinalIgnoreCase)
                        && !LoaderVersion.Contains("pre", StringComparison.OrdinalIgnoreCase)
                        && !LoaderVersion.Contains("rc", StringComparison.OrdinalIgnoreCase);

                    return new LoaderVersionSummary(
                        LoaderType.Forge,
                        GameVersion,
                        LoaderVersion,
                        IsStable);
                })
                .GroupBy(Item => Item.LoaderVersion, StringComparer.OrdinalIgnoreCase)
                .Select(Group => Group.First())
                .ToList();

            return Result<IReadOnlyList<LoaderVersionSummary>>.Success(Versions);
        }
        catch (Exception Exception)
        {
            return Result<IReadOnlyList<LoaderVersionSummary>>.Failure(
                MetadataErrors.InvalidPayload(
                    "Failed to parse Forge Maven metadata.",
                    Exception.Message));
        }
    }
}

public sealed class NeoForgeMetadataClient
{
    private readonly IMetadataHttpClient MetadataHttpClient;

    public NeoForgeMetadataClient(IMetadataHttpClient MetadataHttpClient)
    {
        this.MetadataHttpClient = MetadataHttpClient;
    }

    public async Task<Result<IReadOnlyList<LoaderVersionSummary>>> GetLoaderVersionsAsync(
        VersionId GameVersion,
        CancellationToken CancellationToken)
    {
        var Response = await MetadataHttpClient.GetStringAsync(
            new Uri(MetadataEndpoints.NeoForgeMavenMetadata),
            CancellationToken);

        if (Response.IsFailure)
        {
            return Result<IReadOnlyList<LoaderVersionSummary>>.Failure(Response.Error);
        }

        try
        {
            var Document = XDocument.Parse(Response.Value);
            var GameVersionText = GameVersion.ToString();
            var Prefixes = BuildPrefixes(GameVersionText);

            var Versions = Document
                .Descendants("version")
                .Select(Element => Element.Value?.Trim())
                .Where(Value => !string.IsNullOrWhiteSpace(Value))
                .Where(Value => Prefixes.Any(Prefix => Value!.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)))
                .Select(Value =>
                {
                    var FullVersion = Value!;
                    var IsStable = !FullVersion.Contains("alpha", StringComparison.OrdinalIgnoreCase)
                        && !FullVersion.Contains("beta", StringComparison.OrdinalIgnoreCase)
                        && !FullVersion.Contains("pre", StringComparison.OrdinalIgnoreCase)
                        && !FullVersion.Contains("rc", StringComparison.OrdinalIgnoreCase);

                    return new LoaderVersionSummary(
                        LoaderType.NeoForge,
                        GameVersion,
                        FullVersion,
                        IsStable);
                })
                .GroupBy(Item => Item.LoaderVersion, StringComparer.OrdinalIgnoreCase)
                .Select(Group => Group.First())
                .OrderByDescending(Item => Item.LoaderVersion, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Result<IReadOnlyList<LoaderVersionSummary>>.Success(Versions);
        }
        catch (Exception Exception)
        {
            return Result<IReadOnlyList<LoaderVersionSummary>>.Failure(
                MetadataErrors.InvalidPayload(
                    "Failed to parse NeoForge Maven metadata.",
                    Exception.Message));
        }
    }

    private static IReadOnlyList<string> BuildPrefixes(string GameVersion)
    {
        var Prefixes = new List<string>();

        if (!string.IsNullOrWhiteSpace(GameVersion))
        {
            Prefixes.Add(GameVersion + "-");
            Prefixes.Add(GameVersion + ".");

            var Parts = GameVersion.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (Parts.Length >= 2 && string.Equals(Parts[0], "1", StringComparison.Ordinal))
            {
                var NeoForgeStyle = string.Join(".", Parts.Skip(1));
                Prefixes.Add(NeoForgeStyle + ".");
                Prefixes.Add(NeoForgeStyle + "-");
            }
        }

        return Prefixes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed class QuiltMetadataClient
{
    private readonly IMetadataHttpClient MetadataHttpClient;

    public QuiltMetadataClient(IMetadataHttpClient MetadataHttpClient)
    {
        this.MetadataHttpClient = MetadataHttpClient;
    }

    public async Task<Result<IReadOnlyList<LoaderVersionSummary>>> GetLoaderVersionsAsync(
        VersionId GameVersion,
        CancellationToken CancellationToken)
    {
        var Response = await MetadataHttpClient.GetStringAsync(
            new Uri(MetadataEndpoints.QuiltLoaderVersions(GameVersion.ToString())),
            CancellationToken);

        if (Response.IsFailure)
        {
            return Result<IReadOnlyList<LoaderVersionSummary>>.Failure(Response.Error);
        }

        try
        {
            using var Document = JsonDocument.Parse(Response.Value);

            if (Document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Result<IReadOnlyList<LoaderVersionSummary>>.Failure(
                    MetadataErrors.InvalidPayload("Quilt loader response is not a JSON array."));
            }

            var LoaderVersions = new List<LoaderVersionSummary>();

            foreach (var Entry in Document.RootElement.EnumerateArray())
            {
                if (!Entry.TryGetProperty("loader", out var LoaderElement) || LoaderElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!LoaderElement.TryGetProperty("version", out var VersionElement) || VersionElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var LoaderVersion = VersionElement.GetString();
                if (string.IsNullOrWhiteSpace(LoaderVersion))
                {
                    continue;
                }

                var IsStable = LoaderElement.TryGetProperty("stable", out var StableElement)
                    && StableElement.ValueKind == JsonValueKind.True;

                LoaderVersions.Add(new LoaderVersionSummary(
                    LoaderType.Quilt,
                    GameVersion,
                    LoaderVersion,
                    IsStable));
            }

            return Result<IReadOnlyList<LoaderVersionSummary>>.Success(
                LoaderVersions
                    .GroupBy(Item => Item.LoaderVersion, StringComparer.OrdinalIgnoreCase)
                    .Select(Group => Group.First())
                    .ToList());
        }
        catch (JsonException Exception)
        {
            return Result<IReadOnlyList<LoaderVersionSummary>>.Failure(
                MetadataErrors.InvalidPayload(
                    "Failed to parse Quilt loader metadata.",
                    Exception.Message));
        }
    }
}
