namespace BlockiumLauncher.Domain.ValueObjects;

public readonly record struct VersionId
{
    public string Value { get; }

    public VersionId(string Value)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            throw new ArgumentException("VersionId cannot be null or whitespace.", nameof(Value));
        }

        this.Value = Value.Trim();
    }

    public override string ToString()
    {
        return Value;
    }
}
