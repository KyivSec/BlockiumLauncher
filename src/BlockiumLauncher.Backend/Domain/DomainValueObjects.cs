namespace BlockiumLauncher.Domain.ValueObjects
{
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

        public static VersionId Parse(string value)
        {
            return new VersionId(value);
        }

        public static VersionId Create(string value)
        {
            return Parse(value);
        }
    }
}
