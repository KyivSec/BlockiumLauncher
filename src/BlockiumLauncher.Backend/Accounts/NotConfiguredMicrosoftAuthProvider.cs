using BlockiumLauncher.Application.Abstractions.Auth;
using BlockiumLauncher.Application.UseCases.Accounts;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Auth;

public sealed class NotConfiguredMicrosoftAuthProvider : IMicrosoftAuthProvider
{
    public Task<Result<MicrosoftAuthResult>> SignInAsync(CancellationToken CancellationToken = default)
    {
        return Task.FromResult(Result<MicrosoftAuthResult>.Failure(AccountErrors.MicrosoftAuthNotConfigured));
    }
}
