using BlockiumLauncher.Application.Abstractions.Security;
using BlockiumLauncher.Application.UseCases.Catalog;
using BlockiumLauncher.Application.UseCases.Security;
using BlockiumLauncher.Shared.Results;
using Xunit;

namespace BlockiumLauncher.Tests.Application.Catalog;

public sealed class CurseForgeApiKeyUseCaseTests
{
    [Fact]
    public void ConfigureUseCase_SavesSecretUsingConfiguredName()
    {
        var store = new FakeSecretStore();
        var useCase = new ConfigureCurseForgeApiKeyUseCase(store);

        var result = useCase.Execute(new ConfigureCurseForgeApiKeyRequest
        {
            ApiKey = "abc123"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("curseforge-api-key", store.LastSavedName);
        Assert.Equal("abc123", store.LastSavedValue);
    }

    [Fact]
    public void StatusUseCase_PrefersEnvironmentVariableOverSecureStore()
    {
        var previousValue = System.Environment.GetEnvironmentVariable("BLOCKIUMLAUNCHER_CURSEFORGE_API_KEY");

        try
        {
            System.Environment.SetEnvironmentVariable("BLOCKIUMLAUNCHER_CURSEFORGE_API_KEY", "env-value", EnvironmentVariableTarget.Process);

            var status = new GetCurseForgeApiKeyStatusUseCase(new FakeSecretStore("stored-value")).Execute();

            Assert.Equal("environment", status.EffectiveSource);
            Assert.True(status.EnvironmentVariablePresent);
            Assert.True(status.SecureStoreValuePresent);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("BLOCKIUMLAUNCHER_CURSEFORGE_API_KEY", previousValue, EnvironmentVariableTarget.Process);
        }
    }

    private sealed class FakeSecretStore : ISecretStore
    {
        private readonly string? StoredValue;

        public FakeSecretStore(string? storedValue = null)
        {
            StoredValue = storedValue;
        }

        public string? LastSavedName { get; private set; }
        public string? LastSavedValue { get; private set; }

        public string BackendName => "fake";
        public bool CanPersistSecrets => true;

        public Result SaveSecret(string SecretName, string SecretValue)
        {
            LastSavedName = SecretName;
            LastSavedValue = SecretValue;
            return Result.Success();
        }

        public Result<string> GetSecret(string SecretName)
        {
            return string.IsNullOrWhiteSpace(StoredValue)
                ? Result<string>.Failure(SecretStoreErrors.SecretNotFound)
                : Result<string>.Success(StoredValue);
        }

        public Result DeleteSecret(string SecretName) => Result.Success();
    }
}
