using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Auth;

public interface IMicrosoftAuthProvider
{
    Task<Result<MicrosoftAuthResult>> SignInAsync(CancellationToken CancellationToken = default);
}