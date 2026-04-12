namespace BlockiumLauncher.Domain.Entities;

public sealed class LauncherRuntimeSettings
{
    public int DefaultJavaMajor { get; init; } = 21;
    public bool SkipCompatibilityChecks { get; init; }
    public int DefaultMinMemoryMb { get; init; } = 2048;
    public int DefaultMaxMemoryMb { get; init; } = 4096;

    public static LauncherRuntimeSettings CreateDefault()
    {
        return new LauncherRuntimeSettings();
    }

    public LauncherRuntimeSettings Normalize()
    {
        var normalizedDefaultJavaMajor = DefaultJavaMajor switch
        {
            25 => 25,
            21 => 21,
            17 => 17,
            8 => 8,
            16 => 17,
            _ when DefaultJavaMajor > 21 => 25,
            _ when DefaultJavaMajor > 17 => 21,
            _ when DefaultJavaMajor > 8 => 17,
            _ => 8
        };

        var normalizedMinMemoryMb = DefaultMinMemoryMb <= 0 ? 2048 : DefaultMinMemoryMb;
        var normalizedMaxMemoryMb = DefaultMaxMemoryMb <= 0 ? 4096 : DefaultMaxMemoryMb;
        if (normalizedMinMemoryMb > normalizedMaxMemoryMb)
        {
            (normalizedMinMemoryMb, normalizedMaxMemoryMb) = (normalizedMaxMemoryMb, normalizedMinMemoryMb);
        }

        return new LauncherRuntimeSettings
        {
            DefaultJavaMajor = normalizedDefaultJavaMajor,
            SkipCompatibilityChecks = SkipCompatibilityChecks,
            DefaultMinMemoryMb = normalizedMinMemoryMb,
            DefaultMaxMemoryMb = normalizedMaxMemoryMb
        };
    }
}
