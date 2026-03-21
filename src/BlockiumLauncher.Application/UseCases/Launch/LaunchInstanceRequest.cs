using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Launch;

public sealed class LaunchInstanceRequest
{
    public InstanceId InstanceId { get; }
    public AccountId? AccountId { get; }
    public JavaInstallationId? JavaInstallationId { get; }
    public bool DryRun { get; }

    public LaunchInstanceRequest(
        InstanceId InstanceId,
        AccountId? AccountId = null,
        JavaInstallationId? JavaInstallationId = null,
        bool DryRun = false)
    {
        this.InstanceId = InstanceId;
        this.AccountId = AccountId;
        this.JavaInstallationId = JavaInstallationId;
        this.DryRun = DryRun;
    }
}
