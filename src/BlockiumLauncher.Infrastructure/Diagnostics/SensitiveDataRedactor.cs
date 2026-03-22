using System.Text.RegularExpressions;
using BlockiumLauncher.Application.Abstractions.Diagnostics;

namespace BlockiumLauncher.Infrastructure.Diagnostics;

public sealed class SensitiveDataRedactor : ISecretRedactor
{
    private static readonly Regex JsonSecretPattern = new(
        @"(?i)(""(?:(?:access|refresh|id)?token|authorization|password|secret|clientsecret)""\s*:\s*"")([^""]+)("")",
        RegexOptions.Compiled);

    private static readonly Regex BearerPattern = new(
        "(?i)Bearer\\s+[A-Za-z0-9\\-\\._~\\+\\/]+=*",
        RegexOptions.Compiled);

    public string Redact(string Value)
    {
        if (string.IsNullOrEmpty(Value))
        {
            return Value;
        }

        var Redacted = JsonSecretPattern.Replace(Value, "$1***REDACTED***$3");
        Redacted = BearerPattern.Replace(Redacted, "Bearer ***REDACTED***");
        return Redacted;
    }
}