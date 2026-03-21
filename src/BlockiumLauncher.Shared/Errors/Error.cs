namespace BlockiumLauncher.Shared.Errors;

public sealed record Error
{
    public static readonly Error None = new("None", "No error.");

    public string Code { get; }
    public string Message { get; }
    public string? Details { get; }

    public Error(string Code, string Message, string? Details = null)
    {
        if (string.IsNullOrWhiteSpace(Code))
        {
            throw new ArgumentException("Error code cannot be null or whitespace.", nameof(Code));
        }

        if (string.IsNullOrWhiteSpace(Message))
        {
            throw new ArgumentException("Error message cannot be null or whitespace.", nameof(Message));
        }

        this.Code = Code;
        this.Message = Message;
        this.Details = Details;
    }

    public override string ToString()
    {
        return Details is null
            ? $"{Code}: {Message}"
            : $"{Code}: {Message} ({Details})";
    }
}
