namespace BlockiumLauncher.Application.UseCases.Common;

public sealed class CatalogSearchQuery
{
    public CatalogProvider Provider { get; init; } = CatalogProvider.Modrinth;
    public CatalogContentType ContentType { get; init; }
    public string? Query { get; init; }
    public string? GameVersion { get; init; }
    public string? Loader { get; init; }
    public IReadOnlyList<string> Categories { get; init; } = [];
    public CatalogSearchSort Sort { get; init; } = CatalogSearchSort.Relevance;
    public int Limit { get; init; } = 20;
    public int Offset { get; init; }
}
