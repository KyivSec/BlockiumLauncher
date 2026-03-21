namespace BlockiumLauncher.Domain.ValueObjects;

public readonly record struct JavaInstallationId
{
    public string Value { get; }

    public JavaInstallationId(string Value)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            throw new ArgumentException("JavaInstallationId cannot be null or whitespace.", nameof(Value));
        }

        this.Value = Value.Trim();
    }

    public static JavaInstallationId New()
    {
        return new(Guid.NewGuid().ToString("N"));
    }

    public override string ToString()
    {
        return Value;
    }
}
