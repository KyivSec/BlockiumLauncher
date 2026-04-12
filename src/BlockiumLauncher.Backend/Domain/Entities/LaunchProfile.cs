namespace BlockiumLauncher.Domain.Entities;

public sealed class LaunchProfile
{
    private readonly List<string> ExtraJvmArgsField;
    private readonly List<string> ExtraGameArgsField;
    private readonly Dictionary<string, string> EnvironmentVariablesField;

    public int MinMemoryMb { get; private set; }
    public int MaxMemoryMb { get; private set; }
    public int? PreferredJavaMajor { get; private set; }
    public bool SkipCompatibilityChecks { get; private set; }
    public IReadOnlyList<string> ExtraJvmArgs => ExtraJvmArgsField;
    public IReadOnlyList<string> ExtraGameArgs => ExtraGameArgsField;
    public IReadOnlyDictionary<string, string> EnvironmentVariables => EnvironmentVariablesField;

    public LaunchProfile(
        int MinMemoryMb,
        int MaxMemoryMb,
        IEnumerable<string> ExtraJvmArgs,
        IEnumerable<string> ExtraGameArgs,
        IEnumerable<KeyValuePair<string, string>> EnvironmentVariables,
        int? PreferredJavaMajor = null,
        bool SkipCompatibilityChecks = false)
    {
        ValidateMemory(MinMemoryMb, MaxMemoryMb);
        ValidatePreferredJavaMajor(PreferredJavaMajor);

        ExtraJvmArgsField = NormalizeArgs(ExtraJvmArgs, nameof(ExtraJvmArgs));
        ExtraGameArgsField = NormalizeArgs(ExtraGameArgs, nameof(ExtraGameArgs));
        EnvironmentVariablesField = NormalizeEnvironmentVariables(EnvironmentVariables);

        this.MinMemoryMb = MinMemoryMb;
        this.MaxMemoryMb = MaxMemoryMb;
        this.PreferredJavaMajor = PreferredJavaMajor;
        this.SkipCompatibilityChecks = SkipCompatibilityChecks;
    }

    public static LaunchProfile CreateDefault()
    {
        return CreateDefault(2048, 4096);
    }

    public static LaunchProfile CreateDefault(int minMemoryMb, int maxMemoryMb)
    {
        return new(
            MinMemoryMb: minMemoryMb,
            MaxMemoryMb: maxMemoryMb,
            ExtraJvmArgs: Array.Empty<string>(),
            ExtraGameArgs: Array.Empty<string>(),
            EnvironmentVariables: Array.Empty<KeyValuePair<string, string>>());
    }

    public void WithMemory(int MinMemoryMb, int MaxMemoryMb)
    {
        ValidateMemory(MinMemoryMb, MaxMemoryMb);
        this.MinMemoryMb = MinMemoryMb;
        this.MaxMemoryMb = MaxMemoryMb;
    }

    public void WithJavaPreference(int? preferredJavaMajor, bool skipCompatibilityChecks)
    {
        ValidatePreferredJavaMajor(preferredJavaMajor);
        PreferredJavaMajor = preferredJavaMajor;
        SkipCompatibilityChecks = skipCompatibilityChecks;
    }

    public void WithExtraJvmArgs(IEnumerable<string> Args)
    {
        ExtraJvmArgsField.Clear();
        ExtraJvmArgsField.AddRange(NormalizeArgs(Args, nameof(Args)));
    }

    public void WithExtraGameArgs(IEnumerable<string> Args)
    {
        ExtraGameArgsField.Clear();
        ExtraGameArgsField.AddRange(NormalizeArgs(Args, nameof(Args)));
    }

    public void WithEnvironmentVariables(IEnumerable<KeyValuePair<string, string>> Variables)
    {
        EnvironmentVariablesField.Clear();

        foreach (var Pair in NormalizeEnvironmentVariables(Variables))
        {
            EnvironmentVariablesField.Add(Pair.Key, Pair.Value);
        }
    }

    private static void ValidateMemory(int MinMemoryMb, int MaxMemoryMb)
    {
        if (MinMemoryMb <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinMemoryMb), "MinMemoryMb must be greater than zero.");
        }

        if (MaxMemoryMb <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxMemoryMb), "MaxMemoryMb must be greater than zero.");
        }

        if (MinMemoryMb > MaxMemoryMb)
        {
            throw new ArgumentException("MinMemoryMb cannot be greater than MaxMemoryMb.");
        }
    }

    private static void ValidatePreferredJavaMajor(int? preferredJavaMajor)
    {
        if (preferredJavaMajor is not null && preferredJavaMajor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(preferredJavaMajor), "Preferred Java major must be greater than zero.");
        }
    }

    private static List<string> NormalizeArgs(IEnumerable<string> Args, string ParameterName)
    {
        ArgumentNullException.ThrowIfNull(Args, ParameterName);

        var Result = new List<string>();

        foreach (var Arg in Args)
        {
            if (string.IsNullOrWhiteSpace(Arg))
            {
                throw new ArgumentException("Arguments cannot contain null or whitespace values.", ParameterName);
            }

            Result.Add(Arg.Trim());
        }

        return Result;
    }

    private static Dictionary<string, string> NormalizeEnvironmentVariables(IEnumerable<KeyValuePair<string, string>> Variables)
    {
        ArgumentNullException.ThrowIfNull(Variables);

        var Result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var Pair in Variables)
        {
            if (string.IsNullOrWhiteSpace(Pair.Key))
            {
                throw new ArgumentException("Environment variable keys cannot be null or whitespace.", nameof(Variables));
            }

            Result[Pair.Key.Trim()] = Pair.Value ?? string.Empty;
        }

        return Result;
    }
}
