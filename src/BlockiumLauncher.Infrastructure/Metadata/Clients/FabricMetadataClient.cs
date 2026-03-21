using System.Text.Json;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Metadata;
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

        if (Response.IsFailure) {
            return Result<IReadOnlyList<LoaderVersionSummary>>.Failure(Response.Error);
        }

        try {
            using var Document = JsonDocument.Parse(Response.Value);

            if (Document.RootElement.ValueKind != JsonValueKind.Array) {
                return Result<IReadOnlyList<LoaderVersionSummary>>.Failure(
                    MetadataErrors.InvalidPayload("Fabric loader response is not a JSON array."));
            }

            var LoaderVersions = new List<LoaderVersionSummary>();

            foreach (var Entry in Document.RootElement.EnumerateArray()) {
                if (!Entry.TryGetProperty("loader", out var LoaderElement) || LoaderElement.ValueKind != JsonValueKind.Object) {
                    continue;
                }

                if (!LoaderElement.TryGetProperty("version", out var VersionElement) || VersionElement.ValueKind != JsonValueKind.String) {
                    continue;
                }

                var LoaderVersion = VersionElement.GetString();
                if (string.IsNullOrWhiteSpace(LoaderVersion)) {
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
        catch (JsonException Exception) {
            return Result<IReadOnlyList<LoaderVersionSummary>>.Failure(
                MetadataErrors.InvalidPayload(
                    "Failed to parse Fabric loader metadata.",
                    Exception.Message));
        }
    }
}


