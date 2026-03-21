using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Java;

public sealed class ValidateJavaRequest
{
    public JavaInstallationId JavaInstallationId { get; }

    public ValidateJavaRequest(JavaInstallationId JavaInstallationId)
    {
        this.JavaInstallationId = JavaInstallationId;
    }
}
