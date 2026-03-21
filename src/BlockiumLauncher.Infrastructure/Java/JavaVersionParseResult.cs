using BlockiumLauncher.Domain.Enums;

namespace BlockiumLauncher.Infrastructure.Java;

public sealed record JavaVersionParseResult(
    string Version,
    JavaArchitecture Architecture,
    string Vendor);
