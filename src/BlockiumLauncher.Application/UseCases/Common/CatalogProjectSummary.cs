namespace BlockiumLauncher.Application.UseCases.Common;

public sealed class CatalogProjectSummary
{
    public CatalogProvider Provider { get; init; } = CatalogProvider.Modrinth;
    public CatalogContentType ContentType { get; init; }
    public string ProjectId { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public long Downloads { get; init; }
    public long Follows { get; init; }
    public DateTimeOffset? PublishedAtUtc { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
    public string? IconUrl { get; init; }
    public string? ProjectUrl { get; init; }
    public IReadOnlyList<string> Categories { get; init; } = [];
    public IReadOnlyList<string> GameVersions { get; init; } = [];
    public IReadOnlyList<string> Loaders { get; init; } = [];
}
