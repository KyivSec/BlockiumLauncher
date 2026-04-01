using BlockiumLauncher.Application.Abstractions.Security;
using BlockiumLauncher.Application.UseCases.Security;
using BlockiumLauncher.Backend.Catalog;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Catalog;

public sealed class ConfigureCurseForgeApiKeyRequest
{
    public string ApiKey { get; init; } = string.Empty;
}

public sealed class ConfigureCurseForgeApiKeyUseCase
{
    private readonly ISecretStore SecretStore;

    public ConfigureCurseForgeApiKeyUseCase(ISecretStore secretStore)
    {
        SecretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
    }

    public Result Execute(ConfigureCurseForgeApiKeyRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return Result.Failure(SecretStoreErrors.InvalidRequest);
        }

        return SecretStore.SaveSecret(CurseForgeOptions.ApiKeySecretName, request.ApiKey.Trim());
    }
}

public sealed class ClearCurseForgeApiKeyUseCase
{
    private readonly ISecretStore SecretStore;

    public ClearCurseForgeApiKeyUseCase(ISecretStore secretStore)
    {
        SecretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
    }

    public Result Execute()
    {
        return SecretStore.DeleteSecret(CurseForgeOptions.ApiKeySecretName);
    }
}

public sealed class GetCurseForgeApiKeyStatusUseCase
{
    private readonly ISecretStore SecretStore;

    public GetCurseForgeApiKeyStatusUseCase(ISecretStore secretStore)
    {
        SecretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
    }

    public CurseForgeApiKeyStatus Execute()
    {
        var environmentValue =
            Environment.GetEnvironmentVariable("BLOCKIUMLAUNCHER_CURSEFORGE_API_KEY") ??
            Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");

        var storedResult = SecretStore.GetSecret(CurseForgeOptions.ApiKeySecretName);

        return new CurseForgeApiKeyStatus
        {
            BackendName = SecretStore.BackendName,
            CanPersistSecrets = SecretStore.CanPersistSecrets,
            EnvironmentVariablePresent = !string.IsNullOrWhiteSpace(environmentValue),
            SecureStoreValuePresent = storedResult.IsSuccess,
            EffectiveSource = !string.IsNullOrWhiteSpace(environmentValue)
                ? "environment"
                : storedResult.IsSuccess
                    ? "secure-store"
                    : "missing"
        };
    }
}

public sealed class CurseForgeApiKeyStatus
{
    public string BackendName { get; init; } = string.Empty;
    public bool CanPersistSecrets { get; init; }
    public bool EnvironmentVariablePresent { get; init; }
    public bool SecureStoreValuePresent { get; init; }
    public string EffectiveSource { get; init; } = string.Empty;
}
