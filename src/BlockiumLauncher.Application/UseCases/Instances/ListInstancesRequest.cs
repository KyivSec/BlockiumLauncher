namespace BlockiumLauncher.Application.UseCases.Instances;

public sealed class ListInstancesRequest
{
    public bool IncludeDeleted { get; }

    public ListInstancesRequest(bool IncludeDeleted = false)
    {
        this.IncludeDeleted = IncludeDeleted;
    }
}
