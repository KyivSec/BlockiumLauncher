using System.Text.Json;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Infrastructure.Metadata;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Metadata.Clients;

public sealed class ModrinthContentCatalogService : IContentCatalogProvider, IContentCatalogDetailsProvider, IContentCatalogMetadataProvider, IContentCatalogFileProvider
{
    private static readonly CatalogSearchSort[] SupportedSorts =
    [
        CatalogSearchSort.Relevance,
        CatalogSearchSort.Downloads,
        CatalogSearchSort.Follows,
        CatalogSearchSort.Newest,
        CatalogSearchSort.Updated
    ];

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

    public async Task<Result<CatalogProjectDetails>> GetProjectDetailsAsync(
        CatalogProjectDetailsQuery query,
        CancellationToken cancellationToken = default)
    {
        var response = await metadataHttpClient
            .GetStringAsync(new Uri(MetadataEndpoints.ModrinthProject(query.ProjectId)), cancellationToken)
            .ConfigureAwait(false);

        if (response.IsFailure)
        {
            return Result<CatalogProjectDetails>.Failure(response.Error);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value);
            var root = document.RootElement;

            return Result<CatalogProjectDetails>.Success(new CatalogProjectDetails
            {
                Provider = CatalogProvider.Modrinth,
                ContentType = query.ContentType,
                ProjectId = GetString(root, "id"),
                Slug = GetString(root, "slug"),
                Title = GetString(root, "title"),
                Summary = GetString(root, "description"),
                Author = string.Empty,
                Downloads = GetInt64(root, "downloads"),
                Follows = GetInt64(root, "followers"),
                PublishedAtUtc = GetDateTimeOffset(root, "published"),
                UpdatedAtUtc = GetDateTimeOffset(root, "updated"),
                IconUrl = GetOptionalString(root, "icon_url"),
                ProjectUrl = BuildProjectUrl(GetString(root, "slug")),
                DescriptionFormat = CatalogDescriptionFormat.Markdown,
                DescriptionContent = GetString(root, "body"),
                Categories = GetStringArray(root, "categories"),
                GameVersions = GetStringArray(root, "game_versions"),
                Loaders = GetStringArray(root, "loaders")
            });
        }
        catch (JsonException exception)
        {
            return Result<CatalogProjectDetails>.Failure(
                MetadataErrors.InvalidPayload("Failed to parse the Modrinth project details response.", exception.Message));
        }
    }

    public async Task<Result<CatalogProviderMetadata>> GetMetadataAsync(
        CatalogProviderMetadataQuery query,
        CancellationToken cancellationToken = default)
    {
        var categoryResponse = await metadataHttpClient
            .GetStringAsync(new Uri(MetadataEndpoints.ModrinthCategories), cancellationToken)
            .ConfigureAwait(false);
        if (categoryResponse.IsFailure)
        {
            return Result<CatalogProviderMetadata>.Failure(categoryResponse.Error);
        }

        var gameVersionResponse = await metadataHttpClient
            .GetStringAsync(new Uri(MetadataEndpoints.ModrinthGameVersions), cancellationToken)
            .ConfigureAwait(false);
        if (gameVersionResponse.IsFailure)
        {
            return Result<CatalogProviderMetadata>.Failure(gameVersionResponse.Error);
        }

        var loaderResponse = await metadataHttpClient
            .GetStringAsync(new Uri(MetadataEndpoints.ModrinthLoaders), cancellationToken)
            .ConfigureAwait(false);
        if (loaderResponse.IsFailure)
        {
            return Result<CatalogProviderMetadata>.Failure(loaderResponse.Error);
        }

        try
        {
            using var categoriesDocument = JsonDocument.Parse(categoryResponse.Value);
            using var gameVersionsDocument = JsonDocument.Parse(gameVersionResponse.Value);
            using var loadersDocument = JsonDocument.Parse(loaderResponse.Value);

            var categories = ParseModrinthCategories(categoriesDocument.RootElement, query.ContentType);
            var gameVersions = ParseModrinthGameVersions(gameVersionsDocument.RootElement);
            var loaders = ParseModrinthLoaders(loadersDocument.RootElement, query.ContentType);

            return Result<CatalogProviderMetadata>.Success(new CatalogProviderMetadata
            {
                Provider = CatalogProvider.Modrinth,
                ContentType = query.ContentType,
                SortOptions = SupportedSorts,
                Categories = categories,
                GameVersions = gameVersions,
                Loaders = loaders
            });
        }
        catch (JsonException exception)
        {
            return Result<CatalogProviderMetadata>.Failure(
                MetadataErrors.InvalidPayload("Failed to parse the Modrinth metadata response.", exception.Message));
        }
    }

    public async Task<Result<IReadOnlyList<CatalogFileSummary>>> GetFilesAsync(
        CatalogFileQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query is null || string.IsNullOrWhiteSpace(query.ProjectId))
        {
            return Result<IReadOnlyList<CatalogFileSummary>>.Failure(
                new Shared.Errors.Error("Catalog.InvalidRequest", "A valid Modrinth project id is required."));
        }

        var response = await metadataHttpClient
            .GetStringAsync(new Uri(MetadataEndpoints.ModrinthProjectVersions(query.ProjectId)), cancellationToken)
            .ConfigureAwait(false);
        if (response.IsFailure)
        {
            return Result<IReadOnlyList<CatalogFileSummary>>.Failure(response.Error);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Result<IReadOnlyList<CatalogFileSummary>>.Failure(
                    MetadataErrors.InvalidPayload("Modrinth versions response did not contain a valid array."));
            }

            var gameVersion = NormalizeOptional(query.GameVersion);
            var loader = NormalizeOptional(query.Loader);

            var files = document.RootElement
                .EnumerateArray()
                .Select(version => MapVersionToFileSummary(version, query.ProjectId, query.ContentType))
                .Where(static summary => summary is not null)
                .Cast<CatalogFileSummary>()
                .Where(summary => MatchesFileFilters(summary, gameVersion, loader))
                .OrderByDescending(static summary => summary.PublishedAtUtc)
                .ThenByDescending(static summary => summary.FileId, StringComparer.OrdinalIgnoreCase)
                .Skip(Math.Max(query.Offset, 0))
                .Take(Math.Min(Math.Max(query.Limit, 1), 50))
                .ToArray();

            return Result<IReadOnlyList<CatalogFileSummary>>.Success(files);
        }
        catch (JsonException exception)
        {
            return Result<IReadOnlyList<CatalogFileSummary>>.Failure(
                MetadataErrors.InvalidPayload("Failed to parse the Modrinth version list response.", exception.Message));
        }
    }

    public async Task<Result<CatalogFileSummary>> ResolveFileAsync(
        CatalogFileResolutionQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query is null || string.IsNullOrWhiteSpace(query.ProjectId))
        {
            return Result<CatalogFileSummary>.Failure(
                new Shared.Errors.Error("Catalog.InvalidRequest", "A valid Modrinth project id is required."));
        }

        var fileListResult = await GetFilesAsync(new CatalogFileQuery
        {
            Provider = query.Provider,
            ContentType = query.ContentType,
            ProjectId = query.ProjectId,
            GameVersion = query.GameVersion,
            Loader = query.Loader,
            Limit = 50,
            Offset = 0
        }, cancellationToken).ConfigureAwait(false);

        if (fileListResult.IsFailure)
        {
            return Result<CatalogFileSummary>.Failure(fileListResult.Error);
        }

        var requestedFileId = NormalizeOptional(query.FileId);
        var candidate = !string.IsNullOrWhiteSpace(requestedFileId)
            ? fileListResult.Value.FirstOrDefault(file => string.Equals(file.FileId, requestedFileId, StringComparison.OrdinalIgnoreCase))
            : fileListResult.Value
                .OrderByDescending(file => ScoreFile(file, query.GameVersion, query.Loader))
                .ThenByDescending(static file => file.PublishedAtUtc)
                .FirstOrDefault();

        if (candidate is null)
        {
            return Result<CatalogFileSummary>.Failure(
                MetadataErrors.NotFound("No matching Modrinth file was found for the requested project."));
        }

        return Result<CatalogFileSummary>.Success(candidate);
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

        var loaders = MergeRequestedValues(query.Loaders, query.Loader)
            .Select(static value => $"categories:{value.ToLowerInvariant()}")
            .ToArray();
        if (loaders.Length > 0)
        {
            facets.Add(loaders);
        }

        var versions = MergeRequestedValues(query.GameVersions, query.GameVersion)
            .Select(static value => $"versions:{value}")
            .ToArray();
        if (versions.Length > 0)
        {
            facets.Add(versions);
        }

        var categories = query.Categories
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => $"categories:{value.Trim().ToLowerInvariant()}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (categories.Length > 0)
        {
            facets.Add(categories);
        }

        return facets;
    }

    private static IReadOnlyList<string> MergeRequestedValues(IReadOnlyList<string> values, string? singleValue)
    {
        var merged = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(singleValue) &&
            merged.All(existing => !string.Equals(existing, singleValue.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            merged.Add(singleValue.Trim());
        }

        return merged;
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

    private static IReadOnlyList<string> ParseModrinthCategories(JsonElement root, CatalogContentType contentType)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var targetType = MapContentType(contentType);
        return root
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.Object)
            .Where(item => SupportsProjectType(item, targetType))
            .Select(item => GetString(item, "name"))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ParseModrinthGameVersions(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return root
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.Object)
            .Select(item => GetString(item, "version"))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ParseModrinthLoaders(JsonElement root, CatalogContentType contentType)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var targetType = MapContentType(contentType);
        return root
            .EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.Object)
            .Where(item => SupportsProjectType(item, targetType))
            .Select(item => GetString(item, "name"))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Where(loader => KnownLoaders.Contains(loader, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool SupportsProjectType(JsonElement element, string projectType)
    {
        if (element.TryGetProperty("project_type", out var projectTypeElement) &&
            projectTypeElement.ValueKind == JsonValueKind.String)
        {
            return string.Equals(projectTypeElement.GetString(), projectType, StringComparison.OrdinalIgnoreCase);
        }

        if (element.TryGetProperty("supported_project_types", out var supportedTypes) &&
            supportedTypes.ValueKind == JsonValueKind.Array)
        {
            return supportedTypes
                .EnumerateArray()
                .Any(item => item.ValueKind == JsonValueKind.String &&
                             string.Equals(item.GetString(), projectType, StringComparison.OrdinalIgnoreCase));
        }

        return true;
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

    private static CatalogFileSummary? MapVersionToFileSummary(JsonElement version, string projectId, CatalogContentType contentType)
    {
        var versionId = GetString(version, "id");
        if (string.IsNullOrWhiteSpace(versionId))
        {
            return null;
        }

        if (!version.TryGetProperty("files", out var filesElement) || filesElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement? selectedFile = null;
        foreach (var file in filesElement.EnumerateArray())
        {
            if (selectedFile is null)
            {
                selectedFile = file;
            }

            if (file.TryGetProperty("primary", out var primaryElement) &&
                primaryElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                primaryElement.GetBoolean())
            {
                selectedFile = file;
                break;
            }
        }

        if (selectedFile is null)
        {
            return null;
        }

        var fileElement = selectedFile.Value;
        var fileName = GetString(fileElement, "filename");
        var downloadUrl = GetOptionalString(fileElement, "url");
        var loaders = GetStringArray(version, "loaders");
        var gameVersions = GetStringArray(version, "game_versions");

        return new CatalogFileSummary
        {
            Provider = CatalogProvider.Modrinth,
            ContentType = contentType,
            ProjectId = projectId,
            FileId = versionId,
            DisplayName = GetOptionalString(version, "name") ?? GetOptionalString(version, "version_number") ?? fileName,
            FileName = string.IsNullOrWhiteSpace(fileName) ? $"{versionId}.bin" : fileName,
            DownloadUrl = downloadUrl,
            ProjectUrl = BuildProjectVersionUrl(projectId, versionId),
            FilePageUrl = BuildProjectVersionUrl(projectId, versionId),
            Sha1 = GetHash(fileElement, "sha1"),
            SizeBytes = GetInt64(fileElement, "size"),
            PublishedAtUtc = GetDateTimeOffset(version, "date_published"),
            GameVersions = gameVersions,
            Loaders = loaders,
            IsServerPack = IsLikelyServerPack(fileElement, fileName),
            RequiresManualDownload = string.IsNullOrWhiteSpace(downloadUrl)
        };
    }

    private static bool MatchesFileFilters(CatalogFileSummary summary, string? gameVersion, string? loader)
    {
        if (!string.IsNullOrWhiteSpace(gameVersion) &&
            !summary.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(loader) &&
            !summary.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static int ScoreFile(CatalogFileSummary file, string? gameVersion, string? loader)
    {
        var score = 0;

        if (!string.IsNullOrWhiteSpace(gameVersion) &&
            file.GameVersions.Contains(gameVersion.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            score += 4;
        }

        if (!string.IsNullOrWhiteSpace(loader) &&
            file.Loaders.Contains(loader.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            score += 2;
        }

        if (!file.IsServerPack)
        {
            score += 1;
        }

        return score;
    }

    private static bool IsLikelyServerPack(JsonElement fileElement, string fileName)
    {
        if (fileElement.TryGetProperty("primary", out var primaryElement) &&
            primaryElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            !primaryElement.GetBoolean() &&
            fileName.Contains("server", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return fileName.Contains("server", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetHash(JsonElement fileElement, string hashName)
    {
        if (!fileElement.TryGetProperty("hashes", out var hashesElement) || hashesElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return hashesElement.TryGetProperty(hashName, out var hashElement) && hashElement.ValueKind == JsonValueKind.String
            ? hashElement.GetString()
            : null;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string BuildProjectVersionUrl(string projectId, string versionId)
    {
        return $"https://modrinth.com/project/{Uri.EscapeDataString(projectId)}/version/{Uri.EscapeDataString(versionId)}";
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
