using System.Xml.Linq;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Metadata;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Metadata.Clients;

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