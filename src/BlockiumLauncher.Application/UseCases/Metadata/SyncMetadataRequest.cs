namespace BlockiumLauncher.Application.UseCases.Metadata;

public sealed class SyncMetadataRequest
{
    public bool ForceRefresh { get; }

    public SyncMetadataRequest(bool ForceRefresh = false)
    {
        this.ForceRefresh = ForceRefresh;
    }
}
