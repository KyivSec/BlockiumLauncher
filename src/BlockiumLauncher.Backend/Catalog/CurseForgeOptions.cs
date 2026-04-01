using BlockiumLauncher.Application.Abstractions.Security;
using BlockiumLauncher.Infrastructure.Persistence.Paths;

namespace BlockiumLauncher.Backend.Catalog;

public sealed class CurseForgeOptions
{
    public const string ApiKeySecretName = "curseforge-api-key";

    public string? ApiKey { get; init; }

    public static CurseForgeOptions FromEnvironment()
    {
        return new CurseForgeOptions
        {
            ApiKey = ResolveEnvironmentApiKey()
        };
    }

    public static CurseForgeOptions FromConfiguration(ILauncherPaths launcherPaths, ISecretStore secretStore)
    {
        ArgumentNullException.ThrowIfNull(launcherPaths);
        ArgumentNullException.ThrowIfNull(secretStore);

        var apiKey = ResolveEnvironmentApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var secretResult = secretStore.GetSecret(ApiKeySecretName);
            if (secretResult.IsSuccess)
            {
                apiKey = secretResult.Value;
            }
        }

        return new CurseForgeOptions
        {
            ApiKey = apiKey
        };
    }

    private static string? ResolveEnvironmentApiKey()
    {
        return Environment.GetEnvironmentVariable("BLOCKIUMLAUNCHER_CURSEFORGE_API_KEY") ??
               Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
    }
}
