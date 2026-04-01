using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Backend.Catalog;
using BlockiumLauncher.Infrastructure.Metadata;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Metadata.Clients;

public sealed class CurseForgeContentCatalogService : IContentCatalogProvider, IContentCatalogDetailsProvider, IContentCatalogMetadataProvider, IContentCatalogFileProvider
{
    private const int MinecraftGameId = 432;
    private static readonly CatalogSearchSort[] SupportedSorts =
    [
        CatalogSearchSort.Relevance,
        CatalogSearchSort.Downloads,
        CatalogSearchSort.Follows,
        CatalogSearchSort.Newest,
        CatalogSearchSort.Updated
    ];

    private static readonly string[] SupportedLoaders =
    [
        "forge",
        "fabric",
        "quilt",
        "neoforge"
    ];

    private readonly HttpClient httpClient;
    private readonly CurseForgeOptions options;
    private readonly object sync = new();
    private Dictionary<CatalogContentType, int>? classIdCache;

    public CurseForgeContentCatalogService(HttpClient httpClient, CurseForgeOptions options)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        if (!this.httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BlockiumLauncher/0.1");
        }
        this.httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public CatalogProvider Provider => CatalogProvider.CurseForge;

    public async Task<Result<IReadOnlyList<CatalogProjectSummary>>> SearchAsync(
        CatalogSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var apiKey = options.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Result<IReadOnlyList<CatalogProjectSummary>>.Failure(
                new Error(
                    "Catalog.CurseForgeApiKeyMissing",
                    "CurseForge catalog access requires BLOCKIUMLAUNCHER_CURSEFORGE_API_KEY or CURSEFORGE_API_KEY to be set."));
        }

        var classIdResult = await ResolveClassIdAsync(query.ContentType, apiKey, cancellationToken).ConfigureAwait(false);
        if (classIdResult.IsFailure)
        {
            return Result<IReadOnlyList<CatalogProjectSummary>>.Failure(classIdResult.Error);
        }

        var requestedGameVersions = MergeRequestedValues(query.GameVersions, query.GameVersion);
        var requestedLoaders = MergeRequestedValues(query.Loaders, query.Loader);
        var categoryIdsResult = await ResolveCategoryIdsAsync(classIdResult.Value, query.Categories, apiKey, cancellationToken).ConfigureAwait(false);
        if (categoryIdsResult.IsFailure)
        {
            return Result<IReadOnlyList<CatalogProjectSummary>>.Failure(categoryIdsResult.Error);
        }

        var requestUri = BuildSearchUri(query, classIdResult.Value, categoryIdsResult.Value, requestedGameVersions, requestedLoaders);
        var response = await GetStringAsync(requestUri, apiKey, cancellationToken).ConfigureAwait(false);
        if (response.IsFailure)
        {
            return Result<IReadOnlyList<CatalogProjectSummary>>.Failure(response.Error);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value);
            if (!document.RootElement.TryGetProperty("data", out var dataElement) ||
                dataElement.ValueKind != JsonValueKind.Array)
            {
                return Result<IReadOnlyList<CatalogProjectSummary>>.Failure(
                    MetadataErrors.InvalidPayload("CurseForge search response did not contain a valid data array."));
            }

            var items = new List<CatalogProjectSummary>();

            foreach (var item in dataElement.EnumerateArray())
            {
                var summary = new CatalogProjectSummary
                {
                    Provider = CatalogProvider.CurseForge,
                    ContentType = query.ContentType,
                    ProjectId = GetInt64(item, "id").ToString(),
                    Slug = GetString(item, "slug"),
                    Title = GetString(item, "name"),
                    Description = GetString(item, "summary"),
                    Author = GetPrimaryAuthor(item),
                    Downloads = GetInt64(item, "downloadCount"),
                    Follows = GetInt64(item, "thumbsUpCount"),
                    PublishedAtUtc = GetDateTimeOffset(item, "dateCreated"),
                    UpdatedAtUtc = GetDateTimeOffset(item, "dateModified"),
                    IconUrl = GetNestedString(item, "logo", "url"),
                    ProjectUrl = GetNestedString(item, "links", "websiteUrl"),
                    Categories = GetCategoryNames(item),
                    GameVersions = GetGameVersions(item),
                    Loaders = GetLoaders(item)
                };

                if (!MatchesRequestedFilters(summary, requestedGameVersions, requestedLoaders, query.Categories))
                {
                    continue;
                }

                items.Add(summary);
            }

            return Result<IReadOnlyList<CatalogProjectSummary>>.Success(items);
        }
        catch (JsonException exception)
        {
            return Result<IReadOnlyList<CatalogProjectSummary>>.Failure(
                MetadataErrors.InvalidPayload("Failed to parse the CurseForge catalog response.", exception.Message));
        }
    }

    public async Task<Result<CatalogProjectDetails>> GetProjectDetailsAsync(
        CatalogProjectDetailsQuery query,
        CancellationToken cancellationToken = default)
    {
        var apiKeyResult = ResolveApiKey();
        if (apiKeyResult.IsFailure)
        {
            return Result<CatalogProjectDetails>.Failure(apiKeyResult.Error);
        }

        var projectResponse = await GetStringAsync(new Uri(MetadataEndpoints.CurseForgeMod(query.ProjectId)), apiKeyResult.Value, cancellationToken)
            .ConfigureAwait(false);
        if (projectResponse.IsFailure)
        {
            return Result<CatalogProjectDetails>.Failure(projectResponse.Error);
        }

        var descriptionResponse = await GetStringAsync(new Uri(MetadataEndpoints.CurseForgeModDescription(query.ProjectId)), apiKeyResult.Value, cancellationToken)
            .ConfigureAwait(false);
        if (descriptionResponse.IsFailure)
        {
            return Result<CatalogProjectDetails>.Failure(descriptionResponse.Error);
        }

        try
        {
            using var projectDocument = JsonDocument.Parse(projectResponse.Value);
            using var descriptionDocument = JsonDocument.Parse(descriptionResponse.Value);

            if (!projectDocument.RootElement.TryGetProperty("data", out var projectElement) ||
                projectElement.ValueKind != JsonValueKind.Object)
            {
                return Result<CatalogProjectDetails>.Failure(
                    MetadataErrors.InvalidPayload("CurseForge project response did not contain a valid data object."));
            }

            var descriptionHtml = GetString(descriptionDocument.RootElement, "data");

            return Result<CatalogProjectDetails>.Success(new CatalogProjectDetails
            {
                Provider = CatalogProvider.CurseForge,
                ContentType = query.ContentType,
                ProjectId = GetInt64(projectElement, "id").ToString(),
                Slug = GetString(projectElement, "slug"),
                Title = GetString(projectElement, "name"),
                Summary = GetString(projectElement, "summary"),
                Author = GetPrimaryAuthor(projectElement),
                Downloads = GetInt64(projectElement, "downloadCount"),
                Follows = GetInt64(projectElement, "thumbsUpCount"),
                PublishedAtUtc = GetDateTimeOffset(projectElement, "dateCreated"),
                UpdatedAtUtc = GetDateTimeOffset(projectElement, "dateModified"),
                IconUrl = GetNestedString(projectElement, "logo", "url"),
                ProjectUrl = GetNestedString(projectElement, "links", "websiteUrl"),
                DescriptionFormat = CatalogDescriptionFormat.Html,
                DescriptionContent = descriptionHtml,
                Categories = GetCategoryNames(projectElement),
                GameVersions = GetGameVersions(projectElement),
                Loaders = GetLoaders(projectElement)
            });
        }
        catch (JsonException exception)
        {
            return Result<CatalogProjectDetails>.Failure(
                MetadataErrors.InvalidPayload("Failed to parse the CurseForge project details response.", exception.Message));
        }
    }

    public async Task<Result<CatalogProviderMetadata>> GetMetadataAsync(
        CatalogProviderMetadataQuery query,
        CancellationToken cancellationToken = default)
    {
        var apiKeyResult = ResolveApiKey();
        if (apiKeyResult.IsFailure)
        {
            return Result<CatalogProviderMetadata>.Failure(apiKeyResult.Error);
        }

        var classIdResult = await ResolveClassIdAsync(query.ContentType, apiKeyResult.Value, cancellationToken).ConfigureAwait(false);
        if (classIdResult.IsFailure)
        {
            return Result<CatalogProviderMetadata>.Failure(classIdResult.Error);
        }

        var categoriesUri = new Uri($"{MetadataEndpoints.CurseForgeCategories}?gameId={MinecraftGameId}&classId={classIdResult.Value}");
        var categoriesResponse = await GetStringAsync(categoriesUri, apiKeyResult.Value, cancellationToken).ConfigureAwait(false);
        if (categoriesResponse.IsFailure)
        {
            return Result<CatalogProviderMetadata>.Failure(categoriesResponse.Error);
        }

        try
        {
            using var categoriesDocument = JsonDocument.Parse(categoriesResponse.Value);
            if (!categoriesDocument.RootElement.TryGetProperty("data", out var dataElement) ||
                dataElement.ValueKind != JsonValueKind.Array)
            {
                return Result<CatalogProviderMetadata>.Failure(
                    MetadataErrors.InvalidPayload("CurseForge categories response did not contain a valid data array."));
            }

            var categories = dataElement
                .EnumerateArray()
                .Select(category => GetString(category, "name"))
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Result<CatalogProviderMetadata>.Success(new CatalogProviderMetadata
            {
                Provider = CatalogProvider.CurseForge,
                ContentType = query.ContentType,
                SortOptions = SupportedSorts,
                Categories = categories,
                GameVersions = [],
                Loaders = SupportedLoaders
            });
        }
        catch (JsonException exception)
        {
            return Result<CatalogProviderMetadata>.Failure(
                MetadataErrors.InvalidPayload("Failed to parse the CurseForge metadata response.", exception.Message));
        }
    }

    public async Task<Result<IReadOnlyList<CatalogFileSummary>>> GetFilesAsync(
        CatalogFileQuery query,
        CancellationToken cancellationToken = default)
    {
        var apiKeyResult = ResolveApiKey();
        if (apiKeyResult.IsFailure)
        {
            return Result<IReadOnlyList<CatalogFileSummary>>.Failure(apiKeyResult.Error);
        }

        if (query is null || string.IsNullOrWhiteSpace(query.ProjectId))
        {
            return Result<IReadOnlyList<CatalogFileSummary>>.Failure(
                new Error("Catalog.InvalidRequest", "A valid CurseForge project id is required."));
        }

        if (!int.TryParse(query.ProjectId, out _))
        {
            return Result<IReadOnlyList<CatalogFileSummary>>.Failure(
                new Error("Catalog.InvalidProjectId", "CurseForge project ids must be numeric."));
        }

        var requestUri = BuildFilesUri(query);
        var response = await GetStringAsync(requestUri, apiKeyResult.Value, cancellationToken).ConfigureAwait(false);
        if (response.IsFailure)
        {
            return Result<IReadOnlyList<CatalogFileSummary>>.Failure(response.Error);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value);
            if (!document.RootElement.TryGetProperty("data", out var dataElement) ||
                dataElement.ValueKind != JsonValueKind.Array)
            {
                return Result<IReadOnlyList<CatalogFileSummary>>.Failure(
                    MetadataErrors.InvalidPayload("CurseForge file list response did not contain a valid data array."));
            }

            var items = new List<CatalogFileSummary>();
            foreach (var item in dataElement.EnumerateArray())
            {
                items.Add(MapFileSummary(item, query.ProjectId, query.ContentType));
            }

            return Result<IReadOnlyList<CatalogFileSummary>>.Success(items);
        }
        catch (JsonException exception)
        {
            return Result<IReadOnlyList<CatalogFileSummary>>.Failure(
                MetadataErrors.InvalidPayload("Failed to parse the CurseForge file list response.", exception.Message));
        }
    }

    public async Task<Result<CatalogFileSummary>> ResolveFileAsync(
        CatalogFileResolutionQuery query,
        CancellationToken cancellationToken = default)
    {
        var apiKeyResult = ResolveApiKey();
        if (apiKeyResult.IsFailure)
        {
            return Result<CatalogFileSummary>.Failure(apiKeyResult.Error);
        }

        if (query is null || string.IsNullOrWhiteSpace(query.ProjectId))
        {
            return Result<CatalogFileSummary>.Failure(
                new Error("Catalog.InvalidRequest", "A valid CurseForge project id is required."));
        }

        if (!int.TryParse(query.ProjectId, out _))
        {
            return Result<CatalogFileSummary>.Failure(
                new Error("Catalog.InvalidProjectId", "CurseForge project ids must be numeric."));
        }

        if (!string.IsNullOrWhiteSpace(query.FileId))
        {
            return await GetFileByIdAsync(query, apiKeyResult.Value, cancellationToken).ConfigureAwait(false);
        }

        var listResult = await GetFilesAsync(new CatalogFileQuery
        {
            Provider = query.Provider,
            ContentType = query.ContentType,
            ProjectId = query.ProjectId,
            GameVersion = query.GameVersion,
            Loader = query.Loader,
            Limit = 50,
            Offset = 0
        }, cancellationToken).ConfigureAwait(false);

        if (listResult.IsFailure)
        {
            return Result<CatalogFileSummary>.Failure(listResult.Error);
        }

        var candidate = listResult.Value
            .Where(static item => !item.IsServerPack)
            .OrderByDescending(item => ScoreFile(item, query.GameVersion, query.Loader))
            .ThenByDescending(item => item.PublishedAtUtc)
            .ThenByDescending(item => item.FileId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (candidate is null)
        {
            return Result<CatalogFileSummary>.Failure(
                MetadataErrors.NotFound("No matching CurseForge file was found for the requested project."));
        }

        var downloadUrlResult = await EnsureDownloadUrlAsync(candidate, apiKeyResult.Value, cancellationToken).ConfigureAwait(false);
        if (downloadUrlResult.IsFailure)
        {
            return downloadUrlResult;
        }

        return downloadUrlResult;
    }

    private async Task<Result<int>> ResolveClassIdAsync(CatalogContentType contentType, string apiKey, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (classIdCache is not null && classIdCache.TryGetValue(contentType, out var cachedClassId))
            {
                return Result<int>.Success(cachedClassId);
            }
        }

        var uri = new Uri($"{MetadataEndpoints.CurseForgeCategories}?gameId={MinecraftGameId}&classesOnly=true");
        var response = await GetStringAsync(uri, apiKey, cancellationToken).ConfigureAwait(false);
        if (response.IsFailure)
        {
            return Result<int>.Failure(response.Error);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value);
            if (!document.RootElement.TryGetProperty("data", out var dataElement) ||
                dataElement.ValueKind != JsonValueKind.Array)
            {
                return Result<int>.Failure(MetadataErrors.InvalidPayload("CurseForge categories response did not contain a valid data array."));
            }

            var mapping = new Dictionary<CatalogContentType, int>();
            foreach (var category in dataElement.EnumerateArray())
            {
                var mappedType = MapClass(category);
                if (mappedType is null)
                {
                    continue;
                }

                if (category.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var idValue))
                {
                    mapping[mappedType.Value] = idValue;
                }
            }

            lock (sync)
            {
                classIdCache = mapping;
            }

            if (mapping.TryGetValue(contentType, out var classId))
            {
                return Result<int>.Success(classId);
            }

            return Result<int>.Failure(new Error("Catalog.CurseForgeClassNotFound", $"Could not resolve the CurseForge class for {contentType}."));
        }
        catch (JsonException exception)
        {
            return Result<int>.Failure(MetadataErrors.InvalidPayload("Failed to parse the CurseForge category response.", exception.Message));
        }
    }

    private async Task<Result<IReadOnlyList<int>>> ResolveCategoryIdsAsync(
        int classId,
        IReadOnlyList<string> categories,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var requested = categories
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToList();

        if (requested.Count == 0)
        {
            return Result<IReadOnlyList<int>>.Success([]);
        }

        var uri = new Uri($"{MetadataEndpoints.CurseForgeCategories}?gameId={MinecraftGameId}&classId={classId}");
        var response = await GetStringAsync(uri, apiKey, cancellationToken).ConfigureAwait(false);
        if (response.IsFailure)
        {
            return Result<IReadOnlyList<int>>.Failure(response.Error);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value);
            if (!document.RootElement.TryGetProperty("data", out var dataElement) ||
                dataElement.ValueKind != JsonValueKind.Array)
            {
                return Result<IReadOnlyList<int>>.Failure(MetadataErrors.InvalidPayload("CurseForge categories response did not contain a valid data array."));
            }

            var matches = new List<int>();
            foreach (var category in dataElement.EnumerateArray())
            {
                var name = GetString(category, "name");
                var slug = GetString(category, "slug");
                if (!requested.Any(requestedValue =>
                        string.Equals(requestedValue, name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(requestedValue, slug, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (category.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var idValue))
                {
                    matches.Add(idValue);
                }
            }

            return Result<IReadOnlyList<int>>.Success(matches);
        }
        catch (JsonException exception)
        {
            return Result<IReadOnlyList<int>>.Failure(MetadataErrors.InvalidPayload("Failed to parse the CurseForge category response.", exception.Message));
        }
    }

    private static CatalogContentType? MapClass(JsonElement category)
    {
        var normalizedName = Normalize(GetString(category, "name"));
        var normalizedSlug = Normalize(GetString(category, "slug"));

        if (normalizedName.Contains("modpack", StringComparison.Ordinal) || normalizedSlug.Contains("modpack", StringComparison.Ordinal))
        {
            return CatalogContentType.Modpack;
        }

        if ((normalizedName.Contains("resource", StringComparison.Ordinal) && normalizedName.Contains("pack", StringComparison.Ordinal)) ||
            normalizedSlug.Contains("texture-pack", StringComparison.Ordinal) ||
            normalizedSlug.Contains("resourcepack", StringComparison.Ordinal) ||
            normalizedSlug.Contains("resource-pack", StringComparison.Ordinal))
        {
            return CatalogContentType.ResourcePack;
        }

        if (normalizedName.Contains("shader", StringComparison.Ordinal) || normalizedSlug.Contains("shader", StringComparison.Ordinal))
        {
            return CatalogContentType.Shader;
        }

        if (normalizedName == "mods" || normalizedSlug == "mc-mods" || normalizedSlug == "mods")
        {
            return CatalogContentType.Mod;
        }

        return null;
    }

    private static Uri BuildSearchUri(
        CatalogSearchQuery query,
        int classId,
        IReadOnlyList<int> categoryIds,
        IReadOnlyList<string> requestedGameVersions,
        IReadOnlyList<string> requestedLoaders)
    {
        var parameters = new List<string>
        {
            $"gameId={MinecraftGameId}",
            $"classId={classId}",
            $"pageSize={Math.Min(query.Limit, 50)}",
            $"index={query.Offset}",
            $"sortField={MapSort(query.Sort)}",
            "sortOrder=desc"
        };

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            parameters.Add($"searchFilter={Uri.EscapeDataString(query.Query.Trim())}");
        }

        if (requestedGameVersions.Count == 1)
        {
            parameters.Add($"gameVersion={Uri.EscapeDataString(requestedGameVersions[0])}");
        }

        if (requestedLoaders.Count == 1)
        {
            var loaderType = MapLoader(requestedLoaders[0]);
            if (loaderType is not null)
            {
                parameters.Add($"modLoaderType={loaderType.Value}");
            }
        }

        if (categoryIds.Count > 0)
        {
            parameters.Add($"categoryIds=[{string.Join(",", categoryIds)}]");
        }

        return new Uri($"{MetadataEndpoints.CurseForgeModsSearch}?{string.Join("&", parameters)}");
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

    private static bool MatchesRequestedFilters(
        CatalogProjectSummary summary,
        IReadOnlyList<string> requestedGameVersions,
        IReadOnlyList<string> requestedLoaders,
        IReadOnlyList<string> requestedCategories)
    {
        if (requestedGameVersions.Count > 0 &&
            !summary.GameVersions.Any(version => requestedGameVersions.Contains(version, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (requestedLoaders.Count > 0 &&
            !summary.Loaders.Any(loader => requestedLoaders.Contains(loader, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (requestedCategories.Count > 0 &&
            !summary.Categories.Any(category => requestedCategories.Contains(category, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    private static Uri BuildFilesUri(CatalogFileQuery query)
    {
        var parameters = new List<string>
        {
            $"pageSize={Math.Min(Math.Max(query.Limit, 1), 50)}",
            $"index={Math.Max(query.Offset, 0)}"
        };

        if (!string.IsNullOrWhiteSpace(query.GameVersion))
        {
            parameters.Add($"gameVersion={Uri.EscapeDataString(query.GameVersion.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(query.Loader))
        {
            var loaderType = MapLoader(query.Loader);
            if (loaderType is not null)
            {
                parameters.Add($"modLoaderType={loaderType.Value}");
            }
        }

        return new Uri($"{MetadataEndpoints.CurseForgeModFiles(query.ProjectId)}?{string.Join("&", parameters)}");
    }

    private Result<string> ResolveApiKey()
    {
        var apiKey = options.ApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return Result<string>.Success(apiKey);
        }

        return Result<string>.Failure(
            new Error(
                "Catalog.CurseForgeApiKeyMissing",
                "CurseForge catalog access requires BLOCKIUMLAUNCHER_CURSEFORGE_API_KEY or CURSEFORGE_API_KEY to be set."));
    }

    private async Task<Result<CatalogFileSummary>> GetFileByIdAsync(
        CatalogFileResolutionQuery query,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var requestUri = new Uri(MetadataEndpoints.CurseForgeModFile(query.ProjectId, query.FileId!));
        var response = await GetStringAsync(requestUri, apiKey, cancellationToken).ConfigureAwait(false);
        if (response.IsFailure)
        {
            return Result<CatalogFileSummary>.Failure(response.Error);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value);
            if (!document.RootElement.TryGetProperty("data", out var dataElement) ||
                dataElement.ValueKind != JsonValueKind.Object)
            {
                return Result<CatalogFileSummary>.Failure(
                    MetadataErrors.InvalidPayload("CurseForge file response did not contain a valid data object."));
            }

            var summary = MapFileSummary(dataElement, query.ProjectId, query.ContentType);
            return await EnsureDownloadUrlAsync(summary, apiKey, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            return Result<CatalogFileSummary>.Failure(
                MetadataErrors.InvalidPayload("Failed to parse the CurseForge file response.", exception.Message));
        }
    }

    private async Task<Result<CatalogFileSummary>> EnsureDownloadUrlAsync(
        CatalogFileSummary file,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(file.DownloadUrl))
        {
            return Result<CatalogFileSummary>.Success(file);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(MetadataEndpoints.CurseForgeModFileDownloadUrl(file.ProjectId, file.FileId)));
        request.Headers.Add("x-api-key", apiKey);

        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                try
                {
                    using var document = JsonDocument.Parse(content);
                    var downloadUrl = GetString(document.RootElement, "data");
                    if (string.IsNullOrWhiteSpace(downloadUrl))
                    {
                        return Result<CatalogFileSummary>.Failure(
                            MetadataErrors.NotFound("CurseForge did not return a downloadable URL for the requested file."));
                    }

                    return Result<CatalogFileSummary>.Success(CloneFileSummary(
                        file,
                        downloadUrl: downloadUrl,
                        requiresManualDownload: false,
                        projectUrl: file.ProjectUrl,
                        filePageUrl: file.FilePageUrl));
                }
                catch (JsonException exception)
                {
                    return Result<CatalogFileSummary>.Failure(
                        MetadataErrors.InvalidPayload("Failed to parse the CurseForge download URL response.", exception.Message));
                }
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                var projectUrlResult = await GetProjectWebsiteUrlAsync(file.ProjectId, apiKey, cancellationToken).ConfigureAwait(false);
                if (projectUrlResult.IsFailure)
                {
                    return Result<CatalogFileSummary>.Failure(projectUrlResult.Error);
                }

                var projectUrl = projectUrlResult.Value;
                return Result<CatalogFileSummary>.Success(CloneFileSummary(
                    file,
                    downloadUrl: null,
                    requiresManualDownload: true,
                    projectUrl: projectUrl,
                    filePageUrl: BuildFilePageUrl(projectUrl, file.FileId)));
            }

            return Result<CatalogFileSummary>.Failure(MetadataErrors.HttpFailed(
                $"CurseForge request failed with status code {(int)response.StatusCode}.",
                content));
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<CatalogFileSummary>.Failure(MetadataErrors.Timeout("CurseForge request timed out.", exception.Message));
        }
        catch (Exception exception)
        {
            return Result<CatalogFileSummary>.Failure(
                MetadataErrors.HttpFailed("CurseForge request failed.", exception.Message));
        }
    }

    private static CatalogFileSummary MapFileSummary(JsonElement element, string projectId, CatalogContentType contentType)
    {
        return new CatalogFileSummary
        {
            Provider = CatalogProvider.CurseForge,
            ContentType = contentType,
            ProjectId = projectId,
            FileId = GetInt64(element, "id").ToString(),
            DisplayName = GetString(element, "displayName"),
            FileName = GetString(element, "fileName"),
            DownloadUrl = GetOptionalString(element, "downloadUrl"),
            Sha1 = GetSha1(element),
            SizeBytes = GetInt64(element, "fileLength"),
            PublishedAtUtc = GetDateTimeOffset(element, "fileDate"),
            GameVersions = GetStringArray(element, "gameVersions"),
            Loaders = GetLoadersFromFile(element),
            IsServerPack = GetBoolean(element, "isServerPack"),
            ProjectUrl = null,
            FilePageUrl = null,
            RequiresManualDownload = false
        };
    }

    private async Task<Result<string?>> GetProjectWebsiteUrlAsync(
        string projectId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var response = await GetStringAsync(new Uri(MetadataEndpoints.CurseForgeMod(projectId)), apiKey, cancellationToken).ConfigureAwait(false);
        if (response.IsFailure)
        {
            return Result<string?>.Failure(response.Error);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Value);
            if (!document.RootElement.TryGetProperty("data", out var dataElement) ||
                dataElement.ValueKind != JsonValueKind.Object)
            {
                return Result<string?>.Failure(
                    MetadataErrors.InvalidPayload("CurseForge project response did not contain a valid data object."));
            }

            return Result<string?>.Success(GetNestedString(dataElement, "links", "websiteUrl"));
        }
        catch (JsonException exception)
        {
            return Result<string?>.Failure(
                MetadataErrors.InvalidPayload("Failed to parse the CurseForge project response.", exception.Message));
        }
    }

    private static string? BuildFilePageUrl(string? projectUrl, string fileId)
    {
        if (string.IsNullOrWhiteSpace(projectUrl))
        {
            return null;
        }

        return $"{projectUrl.TrimEnd('/')}/files/{Uri.EscapeDataString(fileId)}";
    }

    private static CatalogFileSummary CloneFileSummary(
        CatalogFileSummary file,
        string? downloadUrl,
        bool requiresManualDownload,
        string? projectUrl,
        string? filePageUrl)
    {
        return new CatalogFileSummary
        {
            Provider = file.Provider,
            ContentType = file.ContentType,
            ProjectId = file.ProjectId,
            FileId = file.FileId,
            DisplayName = file.DisplayName,
            FileName = file.FileName,
            DownloadUrl = downloadUrl,
            ProjectUrl = projectUrl,
            FilePageUrl = filePageUrl,
            Sha1 = file.Sha1,
            SizeBytes = file.SizeBytes,
            PublishedAtUtc = file.PublishedAtUtc,
            GameVersions = file.GameVersions,
            Loaders = file.Loaders,
            IsServerPack = file.IsServerPack,
            RequiresManualDownload = requiresManualDownload
        };
    }

    private static int ScoreFile(CatalogFileSummary item, string? requestedGameVersion, string? requestedLoader)
    {
        var score = 0;

        if (!string.IsNullOrWhiteSpace(requestedGameVersion) &&
            item.GameVersions.Contains(requestedGameVersion.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            score += 4;
        }

        if (!string.IsNullOrWhiteSpace(requestedLoader) &&
            item.Loaders.Contains(requestedLoader.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            score += 2;
        }

        if (!item.IsServerPack)
        {
            score += 1;
        }

        return score;
    }

    private async Task<Result<string>> GetStringAsync(Uri uri, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("x-api-key", apiKey);

        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return Result<string>.Success(content);
            }

            return Result<string>.Failure(MetadataErrors.HttpFailed(
                $"CurseForge request failed with status code {(int)response.StatusCode}.",
                content));
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<string>.Failure(MetadataErrors.Timeout("CurseForge request timed out.", exception.Message));
        }
        catch (Exception exception)
        {
            return Result<string>.Failure(MetadataErrors.HttpFailed("CurseForge request failed.", exception.Message));
        }
    }

    private static int MapSort(CatalogSearchSort sort)
    {
        return sort switch
        {
            CatalogSearchSort.Downloads => 6,
            CatalogSearchSort.Updated => 3,
            CatalogSearchSort.Newest => 11,
            CatalogSearchSort.Follows => 12,
            _ => 2
        };
    }

    private static int? MapLoader(string loader)
    {
        return Normalize(loader) switch
        {
            "forge" => 1,
            "fabric" => 4,
            "quilt" => 5,
            "neoforge" => 6,
            _ => null
        };
    }

    private static string GetPrimaryAuthor(JsonElement element)
    {
        if (!element.TryGetProperty("authors", out var authorsElement) || authorsElement.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var author in authorsElement.EnumerateArray())
        {
            var name = GetString(author, "name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> GetCategoryNames(JsonElement element)
    {
        if (!element.TryGetProperty("categories", out var categoriesElement) || categoriesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return categoriesElement
            .EnumerateArray()
            .Select(category => GetString(category, "name"))
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> GetGameVersions(JsonElement element)
    {
        var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!element.TryGetProperty("latestFilesIndexes", out var filesElement) || filesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        foreach (var file in filesElement.EnumerateArray())
        {
            var gameVersion = GetString(file, "gameVersion");
            if (!string.IsNullOrWhiteSpace(gameVersion))
            {
                versions.Add(gameVersion);
            }
        }

        return versions.ToList();
    }

    private static IReadOnlyList<string> GetLoaders(JsonElement element)
    {
        var loaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!element.TryGetProperty("latestFilesIndexes", out var filesElement) || filesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        foreach (var file in filesElement.EnumerateArray())
        {
            if (!file.TryGetProperty("modLoader", out var modLoaderElement) || !modLoaderElement.TryGetInt32(out var modLoader))
            {
                continue;
            }

            switch (modLoader)
            {
                case 1:
                    loaders.Add("forge");
                    break;
                case 4:
                    loaders.Add("fabric");
                    break;
                case 5:
                    loaders.Add("quilt");
                    break;
                case 6:
                    loaders.Add("neoforge");
                    break;
            }
        }

        return loaders.ToList();
    }

    private static IReadOnlyList<string> GetLoadersFromFile(JsonElement element)
    {
        var loaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in GetStringArray(element, "gameVersions"))
        {
            switch (Normalize(value))
            {
                case "forge":
                    loaders.Add("forge");
                    break;
                case "fabric":
                    loaders.Add("fabric");
                    break;
                case "quilt":
                    loaders.Add("quilt");
                    break;
                case "neoforge":
                case "neo forge":
                    loaders.Add("neoforge");
                    break;
            }
        }

        return loaders.ToList();
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

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string? GetNestedString(JsonElement element, string objectPropertyName, string propertyName)
    {
        return element.TryGetProperty(objectPropertyName, out var objectProperty) &&
               objectProperty.ValueKind == JsonValueKind.Object &&
               objectProperty.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
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

    private static bool GetBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False) &&
               property.GetBoolean();
    }

    private static string? GetSha1(JsonElement element)
    {
        if (!element.TryGetProperty("hashes", out var hashesElement) || hashesElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var hash in hashesElement.EnumerateArray())
        {
            if (GetInt64(hash, "algo") == 1)
            {
                return GetString(hash, "value");
            }
        }

        return null;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(property.GetString(), out var value) ? value : null;
    }
}
