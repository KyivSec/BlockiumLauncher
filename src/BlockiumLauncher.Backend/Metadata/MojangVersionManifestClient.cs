using System.Text.Json;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Metadata.Clients;

public sealed class MojangVersionManifestClient
{
    private readonly IMetadataHttpClient MetadataHttpClient;

    public MojangVersionManifestClient(IMetadataHttpClient MetadataHttpClient)
    {
        this.MetadataHttpClient = MetadataHttpClient;
    }

    public async Task<Result<IReadOnlyList<VersionSummary>>> GetVersionsAsync(CancellationToken CancellationToken)
    {
        var Response = await MetadataHttpClient.GetStringAsync(
            new Uri(MetadataEndpoints.VanillaVersionManifest),
            CancellationToken);

        if (Response.IsFailure)
        {
            return Result<IReadOnlyList<VersionSummary>>.Failure(Response.Error);
        }

        try
        {
            using var Document = JsonDocument.Parse(Response.Value);
            var Root = Document.RootElement;

            if (!Root.TryGetProperty("versions", out var VersionsElement) || VersionsElement.ValueKind != JsonValueKind.Array)
            {
                return Result<IReadOnlyList<VersionSummary>>.Failure(
                    MetadataErrors.InvalidPayload("Vanilla version manifest does not contain a valid versions array."));
            }

            var Versions = new List<VersionSummary>();

            foreach (var VersionElement in VersionsElement.EnumerateArray())
            {
                if (!VersionElement.TryGetProperty("id", out var IdElement) || IdElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var Id = IdElement.GetString();
                if (string.IsNullOrWhiteSpace(Id))
                {
                    continue;
                }

                var Type = VersionElement.TryGetProperty("type", out var TypeElement) && TypeElement.ValueKind == JsonValueKind.String
                    ? TypeElement.GetString() ?? string.Empty
                    : string.Empty;

                var ReleaseTime = VersionElement.TryGetProperty("releaseTime", out var ReleaseTimeElement) && ReleaseTimeElement.ValueKind == JsonValueKind.String
                    ? ReleaseTimeElement.GetString()
                    : null;

                var ReleasedAtUtc = DateTimeOffset.TryParse(ReleaseTime, out var ParsedReleasedAtUtc)
                    ? ParsedReleasedAtUtc
                    : DateTimeOffset.MinValue;

                Versions.Add(new VersionSummary(
                    new VersionId(Id),
                    Id,
                    string.Equals(Type, "release", StringComparison.OrdinalIgnoreCase),
                    ReleasedAtUtc));
            }

            return Result<IReadOnlyList<VersionSummary>>.Success(
                Versions
                    .OrderByDescending(Version => Version.ReleasedAtUtc)
                    .ToList());
        }
        catch (JsonException Exception)
        {
            return Result<IReadOnlyList<VersionSummary>>.Failure(
                MetadataErrors.InvalidPayload(
                    "Failed to parse vanilla version manifest.",
                    Exception.Message));
        }
    }
}
