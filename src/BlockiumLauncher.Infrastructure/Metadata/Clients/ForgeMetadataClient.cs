using System.Xml.Linq;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Metadata;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Metadata.Clients;

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

        if (Response.IsFailure) {
            return Result<IReadOnlyList<LoaderVersionSummary>>.Failure(Response.Error);
        }

        try {
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
        catch (Exception Exception) {
            return Result<IReadOnlyList<LoaderVersionSummary>>.Failure(
                MetadataErrors.InvalidPayload(
                    "Failed to parse Forge Maven metadata.",
                    Exception.Message));
        }
    }
}


