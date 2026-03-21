using System.Text.RegularExpressions;
using BlockiumLauncher.Application.UseCases.Java;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Java;

public static partial class JavaVersionOutputParser
{
    public static Result<JavaVersionParseResult> Parse(string Output)
    {
        if (string.IsNullOrWhiteSpace(Output)) {
            return Result<JavaVersionParseResult>.Failure(
                JavaErrors.Invalid("Java version output was empty."));
        }

        var VersionMatch = VersionRegex().Match(Output);
        if (!VersionMatch.Success) {
            return Result<JavaVersionParseResult>.Failure(
                JavaErrors.Invalid(
                    "Could not parse Java version output.",
                    Output));
        }

        var Version = VersionMatch.Groups["Version"].Value.Trim();
        var Vendor = DetectVendor(Output);
        var Architecture = DetectArchitecture(Output);

        return Result<JavaVersionParseResult>.Success(
            new JavaVersionParseResult(
                Version,
                Architecture,
                Vendor));
    }

    private static string DetectVendor(string Output)
    {
        if (Contains(Output, "Temurin") || Contains(Output, "Adoptium")) {
            return "Eclipse Adoptium";
        }

        if (Contains(Output, "Zulu")) {
            return "Azul";
        }

        if (Contains(Output, "BellSoft") || Contains(Output, "Liberica")) {
            return "BellSoft";
        }

        if (Contains(Output, "Corretto")) {
            return "Amazon";
        }

        if (Contains(Output, "Microsoft")) {
            return "Microsoft";
        }

        if (Contains(Output, "Oracle")) {
            return "Oracle";
        }

        if (Contains(Output, "OpenJDK")) {
            return "OpenJDK";
        }

        return "Unknown";
    }

    private static JavaArchitecture DetectArchitecture(string Output)
    {
        if (Contains(Output, "aarch64") || Contains(Output, "arm64")) {
            return JavaArchitecture.Arm64;
        }

        if (Contains(Output, "64-Bit") || Contains(Output, "x64") || Contains(Output, "amd64")) {
            return JavaArchitecture.X64;
        }

        return JavaArchitecture.Unknown;
    }

    private static bool Contains(string Source, string Value)
    {
        return Source.Contains(Value, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("(?:java|openjdk) version\\s+\"(?<Version>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();
}
