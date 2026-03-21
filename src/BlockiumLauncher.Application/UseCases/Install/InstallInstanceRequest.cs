using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class InstallInstanceRequest
{
    public InstanceId InstanceId { get; }
    public bool ForceRepair { get; }
    public AccountId? AccountId { get; }
    public JavaInstallationId? PreferredJavaInstallationId { get; }

    public InstallInstanceRequest(
        InstanceId InstanceId,
        bool ForceRepair = false,
        AccountId? AccountId = null,
        JavaInstallationId? PreferredJavaInstallationId = null)
    {
        this.InstanceId = InstanceId;
        this.ForceRepair = ForceRepair;
        this.AccountId = AccountId;
        this.PreferredJavaInstallationId = PreferredJavaInstallationId;
    }
}
