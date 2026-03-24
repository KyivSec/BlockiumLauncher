using BlockiumLauncher.Shared.Errors;

namespace BlockiumLauncher.Application.UseCases.Catalog;

public static class CatalogErrors
{
    public static readonly Error InvalidRequest = new("Catalog.InvalidRequest", "The catalog request is invalid.");
}
