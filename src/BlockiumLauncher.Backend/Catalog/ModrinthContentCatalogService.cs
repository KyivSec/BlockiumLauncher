using System.Text.Json;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Infrastructure.Metadata;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Metadata.Clients;

public sealed class ModrinthContentCatalogService : IContentCatalogProvider
{
    private static readonly string[] KnownLoaders =
    [
        "fabric",
        "quilt",
        "forge",
        "neoforge"
    ];

    private readonly IMetadataHttpClient metadataHttpClient;

    public ModrinthContentCatalogService(IMetadataHttpClient metadataHttpClient)
    {
        this.metadataHttpClient = metadataHttpClient ?? throw new ArgumentNullException(nameof(metadataHttpClient));
    }

    public CatalogProvider Provider => CatalogProvider.Modrinth;

    public async Task<Result<IReadOnlyList<CatalogProjectSummary>>> SearchAsync(
        CatalogSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var response = await metadataHttpClient
            .GetStringAsync(BuildSearchUri(query), cancellationToken)
            .ConfigureAwait(false);

        if (response.IsFailure)
        {
            return Result<IReadOnlyList<CatalogProjectSummary>>.Failure(response.Error);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value);
            if (!document.RootElement.TryGetProperty("hits", out var hitsElement) ||
                hitsElement.ValueKind != JsonValueKind.Array)
            {
                return Result<IReadOnlyList<CatalogProjectSummary>>.Failure(
                    MetadataErrors.InvalidPayload("Modrinth search response did not contain a valid hits array."));
            }

            var items = new List<CatalogProjectSummary>();

            foreach (var hit in hitsElement.EnumerateArray())
            {
                items.Add(new CatalogProjectSummary
                {
                    Provider = CatalogProvider.Modrinth,
                    ContentType = query.ContentType,
                    ProjectId = GetString(hit, "project_id"),
                    Slug = GetString(hit, "slug"),
                    Title = GetString(hit, "title"),
                    Description = GetString(hit, "description"),
                    Author = GetString(hit, "author"),
                    Downloads = GetInt64(hit, "downloads"),
                    Follows = GetInt64(hit, "follows"),
                    PublishedAtUtc = GetDateTimeOffset(hit, "date_created"),
                    UpdatedAtUtc = GetDateTimeOffset(hit, "date_modified"),
                    IconUrl = GetOptionalString(hit, "icon_url"),
                    ProjectUrl = BuildProjectUrl(GetString(hit, "slug")),
                    Categories = GetStringArray(hit, "display_categories"),
                    GameVersions = GetStringArray(hit, "versions"),
                    Loaders = GetLoaders(hit)
                });
            }

            return Result<IReadOnlyList<CatalogProjectSummary>>.Success(items);
        }
        catch (JsonException exception)
        {
            return Result<IReadOnlyList<CatalogProjectSummary>>.Failure(
                MetadataErrors.InvalidPayload("Failed to parse the Modrinth catalog response.", exception.Message));
        }
    }

    private static Uri BuildSearchUri(CatalogSearchQuery query)
    {
        var parameters = new List<string>
        {
            $"limit={query.Limit}",
            $"offset={query.Offset}",
            $"index={Uri.EscapeDataString(MapSort(query.Sort))}",
            $"facets={Uri.EscapeDataString(JsonSerializer.Serialize(BuildFacets(query)))}"
        };

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            parameters.Add($"query={Uri.EscapeDataString(query.Query.Trim())}");
        }

        return new Uri($"{MetadataEndpoints.ModrinthSearch}?{string.Join("&", parameters)}");
    }

    private static IReadOnlyList<IReadOnlyList<string>> BuildFacets(CatalogSearchQuery query)
    {
        var facets = new List<IReadOnlyList<string>>
        {
            new[] { $"project_type:{MapContentType(query.ContentType)}" }
        };

        if (!string.IsNullOrWhiteSpace(query.Loader))
        {
            facets.Add(new[] { $"categories:{query.Loader.Trim().ToLowerInvariant()}" });
        }

        if (!string.IsNullOrWhiteSpace(query.GameVersion))
        {
            facets.Add(new[] { $"versions:{query.GameVersion.Trim()}" });
        }

        foreach (var category in query.Categories
                     .Where(static value => !string.IsNullOrWhiteSpace(value))
                     .Select(static value => value.Trim().ToLowerInvariant()))
        {
            facets.Add(new[] { $"categories:{category}" });
        }

        return facets;
    }

    private static string MapContentType(CatalogContentType contentType)
    {
        return contentType switch
        {
            CatalogContentType.Mod => "mod",
            CatalogContentType.Modpack => "modpack",
            CatalogContentType.ResourcePack => "resourcepack",
            CatalogContentType.Shader => "shader",
            _ => "mod"
        };
    }

    private static string MapSort(CatalogSearchSort sort)
    {
        return sort switch
        {
            CatalogSearchSort.Downloads => "downloads",
            CatalogSearchSort.Follows => "follows",
            CatalogSearchSort.Newest => "newest",
            CatalogSearchSort.Updated => "updated",
            _ => "relevance"
        };
    }

    private static string BuildProjectUrl(string slug)
    {
        return string.IsNullOrWhiteSpace(slug)
            ? "https://modrinth.com"
            : $"https://modrinth.com/project/{slug}";
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return GetOptionalString(element, propertyName) ?? string.Empty;
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static long GetInt64(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt64(out var value)
            ? value
            : 0;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(property.GetString(), out var value) ? value : null;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString() ?? string.Empty)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> GetLoaders(JsonElement element)
    {
        var categories = GetStringArray(element, "categories");
        return categories
            .Where(category => KnownLoaders.Contains(category, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
