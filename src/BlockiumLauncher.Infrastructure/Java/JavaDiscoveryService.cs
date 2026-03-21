using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Java;

public sealed class JavaDiscoveryService : IJavaDiscoveryService
{
    private readonly IJavaValidationService JavaValidationService;
    private readonly JavaDiscoveryOptions Options;

    public JavaDiscoveryService(
        IJavaValidationService JavaValidationService,
        JavaDiscoveryOptions Options)
    {
        this.JavaValidationService = JavaValidationService;
        this.Options = Options;
    }

    public async Task<Result<IReadOnlyList<JavaInstallation>>> DiscoverAsync(
        bool IncludeInvalid,
        CancellationToken CancellationToken)
    {
        var Candidates = EnumerateCandidatePaths()
            .Select(NormalizePath)
            .Where(Item => !string.IsNullOrWhiteSpace(Item))
            .Distinct(GetPathComparer())
            .ToList();

        var Installations = new List<JavaInstallation>();

        foreach (var Candidate in Candidates) {
            CancellationToken.ThrowIfCancellationRequested();

            var ValidationResult = await JavaValidationService.ValidateExecutableAsync(
                Candidate,
                CancellationToken);

            if (ValidationResult.IsSuccess) {
                Installations.Add(ValidationResult.Value);
                continue;
            }

            if (IncludeInvalid) {
                Installations.Add(CreateInvalidInstallation(Candidate));
            }
        }

        return Result<IReadOnlyList<JavaInstallation>>.Success(Installations);
    }

    private IEnumerable<string> EnumerateCandidatePaths()
    {
        foreach (var BundledCandidate in EnumerateBundledCandidates()) {
            yield return BundledCandidate;
        }

        foreach (var EnvironmentCandidate in EnumerateEnvironmentCandidates()) {
            yield return EnvironmentCandidate;
        }

        foreach (var CommonCandidate in EnumerateCommonCandidates()) {
            yield return CommonCandidate;
        }
    }

    private IEnumerable<string> EnumerateBundledCandidates()
    {
        var BaseDirectory = AppContext.BaseDirectory;
        var ExecutableName = GetJavaExecutableName();

        foreach (var RelativeDirectory in Options.BundledRelativeDirectories) {
            yield return Path.Combine(BaseDirectory, RelativeDirectory, ExecutableName);
            yield return Path.Combine(BaseDirectory, RelativeDirectory, "bin", ExecutableName);
        }
    }

    private IEnumerable<string> EnumerateEnvironmentCandidates()
    {
        var ExecutableName = GetJavaExecutableName();

        var JavaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(JavaHome)) {
            yield return Path.Combine(JavaHome, "bin", ExecutableName);
        }

        var PathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(PathValue)) {
            yield break;
        }

        var Segments = PathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var Segment in Segments) {
            yield return Path.Combine(Segment, ExecutableName);
        }
    }

    private IEnumerable<string> EnumerateCommonCandidates()
    {
        if (OperatingSystem.IsWindows()) {
            foreach (var Root in Options.WindowsCommonRoots) {
                if (!Directory.Exists(Root)) {
                    continue;
                }

                foreach (var Candidate in EnumerateChildJavaCandidates(Root)) {
                    yield return Candidate;
                }
            }

            yield break;
        }

        yield return "/usr/bin/java";
        yield return "/usr/local/bin/java";
        yield return "/opt/java/bin/java";
    }

    private IEnumerable<string> EnumerateChildJavaCandidates(string Root)
    {
        var ExecutableName = GetJavaExecutableName();
        var Candidates = new List<string>();

        try {
            foreach (var DirectoryPath in Directory.EnumerateDirectories(Root)) {
                Candidates.Add(Path.Combine(DirectoryPath, "bin", ExecutableName));

                foreach (var NestedDirectoryPath in Directory.EnumerateDirectories(DirectoryPath)) {
                    Candidates.Add(Path.Combine(NestedDirectoryPath, "bin", ExecutableName));
                }
            }
        }
        catch {
            return Array.Empty<string>();
        }

        return Candidates;
    }

    private static JavaInstallation CreateInvalidInstallation(string ExecutablePath)
    {
        return JavaInstallation.Create(
            JavaInstallationId.New(),
            ExecutablePath,
            "unknown",
            JavaArchitecture.Unknown,
            "Unknown",
            false);
    }

    private static string NormalizePath(string PathValue)
    {
        if (string.IsNullOrWhiteSpace(PathValue)) {
            return string.Empty;
        }

        try {
            return Path.GetFullPath(PathValue.Trim());
        }
        catch {
            return PathValue.Trim();
        }
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    private static string GetJavaExecutableName()
    {
        return OperatingSystem.IsWindows() ? "java.exe" : "java";
    }
}