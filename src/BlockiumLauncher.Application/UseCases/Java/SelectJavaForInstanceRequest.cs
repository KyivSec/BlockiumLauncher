using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Java;

public sealed class SelectJavaForInstanceRequest
{
    public InstanceId InstanceId { get; }
    public JavaInstallationId JavaInstallationId { get; }

    public SelectJavaForInstanceRequest(InstanceId InstanceId, JavaInstallationId JavaInstallationId)
    {
        this.InstanceId = InstanceId;
        this.JavaInstallationId = JavaInstallationId;
    }
}