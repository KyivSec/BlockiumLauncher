namespace BlockiumLauncher.Domain.ValueObjects;

public readonly record struct AccountId
{
    public string Value { get; }

    public AccountId(string Value)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            throw new ArgumentException("AccountId cannot be null or whitespace.", nameof(Value));
        }

        this.Value = Value.Trim();
    }

    public static AccountId New()
    {
        return new(Guid.NewGuid().ToString("N"));
    }

    public override string ToString()
    {
        return Value;
    }
}
