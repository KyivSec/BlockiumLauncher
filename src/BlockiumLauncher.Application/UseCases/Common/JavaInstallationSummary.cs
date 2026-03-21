using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Common;

public sealed record JavaInstallationSummary(
    JavaInstallationId JavaInstallationId,
    string ExecutablePath,
    string Version,
    JavaArchitecture Architecture,
    string Vendor,
    bool IsValid);
