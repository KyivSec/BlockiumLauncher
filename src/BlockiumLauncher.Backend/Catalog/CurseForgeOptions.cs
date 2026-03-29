namespace BlockiumLauncher.Backend.Catalog;

public sealed class CurseForgeOptions
{
    public string? ApiKey { get; init; }

    public static CurseForgeOptions FromEnvironment()
    {
        return new CurseForgeOptions
        {
            ApiKey =
                Environment.GetEnvironmentVariable("BLOCKIUMLAUNCHER_CURSEFORGE_API_KEY") ??
                Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY")
        };
    }
}
