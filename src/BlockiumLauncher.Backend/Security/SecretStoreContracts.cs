using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Security
{
    public interface ISecretStore
    {
        string BackendName { get; }
        bool CanPersistSecrets { get; }

        Result SaveSecret(string SecretName, string SecretValue);
        Result<string> GetSecret(string SecretName);
        Result DeleteSecret(string SecretName);
    }
}

namespace BlockiumLauncher.Application.UseCases.Security
{
    public static class SecretStoreErrors
    {
        public static readonly Error InvalidRequest = new("SecretStore.InvalidRequest", "The secret store request is invalid.");
        public static readonly Error SecretNotFound = new("SecretStore.SecretNotFound", "The requested secret was not found.");
        public static readonly Error StoreUnavailable = new("SecretStore.StoreUnavailable", "The secure secret store is not available on this system.");
        public static readonly Error PersistenceFailed = new("SecretStore.PersistenceFailed", "The secure secret store operation failed.");
    }
}
