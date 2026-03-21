namespace BlockiumLauncher.Application.UseCases.Java;

public sealed class DiscoverJavaRequest
{
    public bool IncludeInvalid { get; }

    public DiscoverJavaRequest(bool IncludeInvalid = false)
    {
        this.IncludeInvalid = IncludeInvalid;
    }
}
