namespace BlockiumLauncher.Domain.ValueObjects;

public readonly record struct InstanceId
{
    public string Value { get; }

    public InstanceId(string Value)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            throw new ArgumentException("InstanceId cannot be null or whitespace.", nameof(Value));
        }

        this.Value = Value.Trim();
    }

    public static InstanceId New()
    {
        return new(Guid.NewGuid().ToString("N"));
    }

    public override string ToString()
    {
        return Value;
    }
}
