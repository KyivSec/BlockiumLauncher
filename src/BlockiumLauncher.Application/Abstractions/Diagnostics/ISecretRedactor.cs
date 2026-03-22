namespace BlockiumLauncher.Application.Abstractions.Diagnostics;

public interface ISecretRedactor
{
    string Redact(string Value);
}