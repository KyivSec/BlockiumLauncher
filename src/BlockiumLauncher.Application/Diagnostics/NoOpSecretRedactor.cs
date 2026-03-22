using BlockiumLauncher.Application.Abstractions.Diagnostics;

namespace BlockiumLauncher.Application.Diagnostics;

public sealed class NoOpSecretRedactor : ISecretRedactor
{
    public static readonly NoOpSecretRedactor Instance = new();

    public string Redact(string Value)
    {
        return Value;
    }
}