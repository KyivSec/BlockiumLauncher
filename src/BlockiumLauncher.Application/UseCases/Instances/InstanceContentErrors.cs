using BlockiumLauncher.Shared.Errors;

namespace BlockiumLauncher.Application.UseCases.Instances;

public static class InstanceContentErrors
{
    public static readonly Error InvalidRequest = new("Instances.InvalidRequest", "The instance content request is invalid.");
    public static readonly Error InstanceNotFound = new("Instances.InstanceNotFound", "The requested instance was not found.");
    public static readonly Error ContentNotFound = new("Instances.ContentNotFound", "The requested content file was not found.");
}
