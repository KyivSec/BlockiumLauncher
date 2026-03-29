using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using BlockiumLauncher.Application.UseCases.Java;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Java;

public interface IJavaVersionProbe
{
    Task<Result<JavaVersionParseResult>> ProbeAsync(string ExecutablePath, CancellationToken CancellationToken);
}

public sealed class JavaDiscoveryOptions
{
    public IReadOnlyList<string> BundledRelativeDirectories { get; init; } = new[]
    {
        "java",
        "runtime",
        "runtimes/java",
        "runtimes/runtime",
        "bin"
    };

    public IReadOnlyList<string> WindowsCommonRoots { get; init; } = new[]
    {
        @"C:\Program Files\Java",
        @"C:\Program Files\Eclipse Adoptium",
        @"C:\Program Files\Microsoft",
        @"C:\Program Files\Zulu",
        @"C:\Program Files\BellSoft",
        @"C:\Program Files\Amazon Corretto",
        @"C:\Program Files (x86)\Java",
        @"C:\Program Files (x86)\Eclipse Adoptium",
        @"C:\Program Files (x86)\Microsoft",
        @"C:\Program Files (x86)\Zulu",
        @"C:\Program Files (x86)\BellSoft",
        @"C:\Program Files (x86)\Amazon Corretto"
    };
}

public sealed record JavaVersionParseResult(
    string Version,
    JavaArchitecture Architecture,
    string Vendor);

public static partial class JavaVersionOutputParser
{
    public static Result<JavaVersionParseResult> Parse(string Output)
    {
        if (string.IsNullOrWhiteSpace(Output))
        {
            return Result<JavaVersionParseResult>.Failure(
                JavaErrors.Invalid("Java version output was empty."));
        }

        var VersionMatch = VersionRegex().Match(Output);
        if (!VersionMatch.Success)
        {
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
        if (Contains(Output, "Temurin") || Contains(Output, "Adoptium"))
        {
            return "Eclipse Adoptium";
        }

        if (Contains(Output, "Zulu"))
        {
            return "Azul";
        }

        if (Contains(Output, "BellSoft") || Contains(Output, "Liberica"))
        {
            return "BellSoft";
        }

        if (Contains(Output, "Corretto"))
        {
            return "Amazon";
        }

        if (Contains(Output, "Microsoft"))
        {
            return "Microsoft";
        }

        if (Contains(Output, "Oracle"))
        {
            return "Oracle";
        }

        if (Contains(Output, "OpenJDK"))
        {
            return "OpenJDK";
        }

        return "Unknown";
    }

    private static JavaArchitecture DetectArchitecture(string Output)
    {
        if (Contains(Output, "aarch64") || Contains(Output, "arm64"))
        {
            return JavaArchitecture.Arm64;
        }

        if (Contains(Output, "64-Bit") || Contains(Output, "x64") || Contains(Output, "amd64"))
        {
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

public sealed class JavaVersionProbe : IJavaVersionProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    public async Task<Result<JavaVersionParseResult>> ProbeAsync(string ExecutablePath, CancellationToken CancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ExecutablePath))
        {
            return Result<JavaVersionParseResult>.Failure(
                JavaErrors.NotFound("Java executable path is empty."));
        }

        if (!File.Exists(ExecutablePath))
        {
            return Result<JavaVersionParseResult>.Failure(
                JavaErrors.NotFound(
                    "Java executable was not found.",
                    ExecutablePath));
        }

        var StartInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            Arguments = "-version",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var Process = new Process
            {
                StartInfo = StartInfo
            };

            var Started = Process.Start();
            if (!Started)
            {
                return Result<JavaVersionParseResult>.Failure(
                    JavaErrors.VersionProbeFailed(
                        "Java process could not be started.",
                        ExecutablePath));
            }

            var StandardOutputTask = Process.StandardOutput.ReadToEndAsync(CancellationToken);
            var StandardErrorTask = Process.StandardError.ReadToEndAsync(CancellationToken);

            using var TimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
            TimeoutCts.CancelAfter(ProbeTimeout);

            try
            {
                await Process.WaitForExitAsync(TimeoutCts.Token);
            }
            catch (OperationCanceledException) when (!CancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!Process.HasExited)
                    {
                        Process.Kill(true);
                    }
                }
                catch
                {
                }

                return Result<JavaVersionParseResult>.Failure(
                    JavaErrors.Timeout(
                        "Java version probe timed out.",
                        ExecutablePath));
            }

            var StandardOutput = await StandardOutputTask;
            var StandardError = await StandardErrorTask;

            var CombinedOutput = BuildCombinedOutput(StandardOutput, StandardError);
            var ParseResult = JavaVersionOutputParser.Parse(CombinedOutput);

            if (ParseResult.IsFailure)
            {
                return Result<JavaVersionParseResult>.Failure(ParseResult.Error);
            }

            return Result<JavaVersionParseResult>.Success(ParseResult.Value);
        }
        catch (UnauthorizedAccessException Exception)
        {
            return Result<JavaVersionParseResult>.Failure(
                JavaErrors.AccessDenied(
                    "Access was denied while probing Java.",
                    Exception.Message));
        }
        catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception Exception)
        {
            return Result<JavaVersionParseResult>.Failure(
                JavaErrors.VersionProbeFailed(
                    "Failed to probe Java version.",
                    Exception.Message));
        }
    }

    private static string BuildCombinedOutput(string StandardOutput, string StandardError)
    {
        var Builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(StandardOutput))
        {
            Builder.AppendLine(StandardOutput.Trim());
        }

        if (!string.IsNullOrWhiteSpace(StandardError))
        {
            Builder.AppendLine(StandardError.Trim());
        }

        return Builder.ToString().Trim();
    }
}
