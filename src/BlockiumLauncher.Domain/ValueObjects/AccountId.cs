namespace BlockiumLauncher.Domain.ValueObjects;

public readonly record struct AccountId
{
    public string Value { get; }

    public AccountId(Guid Value)
    {
        this.Value = Value.ToString("N");
    }

    public AccountId(string Value)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(Value));
        }

        this.Value = Value.Trim();
    }

    public static AccountId New()
    {
        return new AccountId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value;
    }
}