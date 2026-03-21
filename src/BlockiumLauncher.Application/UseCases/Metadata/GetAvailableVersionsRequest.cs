using BlockiumLauncher.Domain.Enums;

namespace BlockiumLauncher.Application.UseCases.Metadata;

public sealed class GetAvailableVersionsRequest
{
    public bool IncludeSnapshots { get; }
    public LoaderType? LoaderTypeFilter { get; }

    public GetAvailableVersionsRequest(bool IncludeSnapshots = false, LoaderType? LoaderTypeFilter = null)
    {
        this.IncludeSnapshots = IncludeSnapshots;
        this.LoaderTypeFilter = LoaderTypeFilter;
    }
}
