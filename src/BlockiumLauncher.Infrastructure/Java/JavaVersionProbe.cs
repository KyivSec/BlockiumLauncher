using System.Diagnostics;
using System.Text;
using BlockiumLauncher.Application.UseCases.Java;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Java;

public sealed class JavaVersionProbe : IJavaVersionProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    public async Task<Result<JavaVersionParseResult>> ProbeAsync(string ExecutablePath, CancellationToken CancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ExecutablePath)) {
            return Result<JavaVersionParseResult>.Failure(
                JavaErrors.NotFound("Java executable path is empty."));
        }

        if (!File.Exists(ExecutablePath)) {
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

        try {
            using var Process = new Process
            {
                StartInfo = StartInfo
            };

            var Started = Process.Start();
            if (!Started) {
                return Result<JavaVersionParseResult>.Failure(
                    JavaErrors.VersionProbeFailed(
                        "Java process could not be started.",
                        ExecutablePath));
            }

            var StandardOutputTask = Process.StandardOutput.ReadToEndAsync(CancellationToken);
            var StandardErrorTask = Process.StandardError.ReadToEndAsync(CancellationToken);

            using var TimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
            TimeoutCts.CancelAfter(ProbeTimeout);

            try {
                await Process.WaitForExitAsync(TimeoutCts.Token);
            }
            catch (OperationCanceledException) when (!CancellationToken.IsCancellationRequested) {
                try {
                    if (!Process.HasExited) {
                        Process.Kill(true);
                    }
                }
                catch {
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

            if (ParseResult.IsFailure) {
                return Result<JavaVersionParseResult>.Failure(ParseResult.Error);
            }

            return Result<JavaVersionParseResult>.Success(ParseResult.Value);
        }
        catch (UnauthorizedAccessException Exception) {
            return Result<JavaVersionParseResult>.Failure(
                JavaErrors.AccessDenied(
                    "Access was denied while probing Java.",
                    Exception.Message));
        }
        catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception Exception) {
            return Result<JavaVersionParseResult>.Failure(
                JavaErrors.VersionProbeFailed(
                    "Failed to probe Java version.",
                    Exception.Message));
        }
    }

    private static string BuildCombinedOutput(string StandardOutput, string StandardError)
    {
        var Builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(StandardOutput)) {
            Builder.AppendLine(StandardOutput.Trim());
        }

        if (!string.IsNullOrWhiteSpace(StandardError)) {
            Builder.AppendLine(StandardError.Trim());
        }

        return Builder.ToString().Trim();
    }
}
