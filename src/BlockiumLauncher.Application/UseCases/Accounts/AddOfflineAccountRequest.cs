namespace BlockiumLauncher.Application.UseCases.Accounts;

public sealed class AddOfflineAccountRequest
{
    public string DisplayName { get; }
    public bool SetAsDefault { get; }

    public AddOfflineAccountRequest(string DisplayName, bool SetAsDefault = true)
    {
        this.DisplayName = NormalizeRequired(DisplayName, nameof(DisplayName));
        this.SetAsDefault = SetAsDefault;
    }

    private static string NormalizeRequired(string Value, string ParamName)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", ParamName);
        }

        return Value.Trim();
    }
}
