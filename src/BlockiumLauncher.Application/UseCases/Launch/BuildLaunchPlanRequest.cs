using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Launch;

public sealed class BuildLaunchPlanRequest
{
    public InstanceId InstanceId { get; }
    public AccountId? AccountId { get; }
    public JavaInstallationId? JavaInstallationId { get; }

    public BuildLaunchPlanRequest(
        InstanceId InstanceId,
        AccountId? AccountId = null,
        JavaInstallationId? JavaInstallationId = null)
    {
        this.InstanceId = InstanceId;
        this.AccountId = AccountId;
        this.JavaInstallationId = JavaInstallationId;
    }
}
