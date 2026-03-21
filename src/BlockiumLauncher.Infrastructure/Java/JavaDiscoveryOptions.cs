namespace BlockiumLauncher.Infrastructure.Java;

public sealed class JavaDiscoveryOptions
{
    public IReadOnlyList<string> BundledRelativeDirectories { get; init; } = new[]
    {
        "java",
        "runtime",
        "runtimes/java",
        "runtimes/runtime",
        "bin"
    };

    public IReadOnlyList<string> WindowsCommonRoots { get; init; } = new[]
    {
        @"C:\Program Files\Java",
        @"C:\Program Files\Eclipse Adoptium",
        @"C:\Program Files\Microsoft",
        @"C:\Program Files\Zulu",
        @"C:\Program Files\BellSoft",
        @"C:\Program Files\Amazon Corretto",
        @"C:\Program Files (x86)\Java",
        @"C:\Program Files (x86)\Eclipse Adoptium",
        @"C:\Program Files (x86)\Microsoft",
        @"C:\Program Files (x86)\Zulu",
        @"C:\Program Files (x86)\BellSoft",
        @"C:\Program Files (x86)\Amazon Corretto"
    };
}
