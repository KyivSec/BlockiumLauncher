using BlockiumLauncher.Application.Abstractions.Auth;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Accounts;

public sealed class SignInMicrosoftUseCase
{
    private readonly IMicrosoftAuthProvider MicrosoftAuthProvider;
    private readonly AddAccountUseCase AddAccountUseCase;

    public SignInMicrosoftUseCase(
        IMicrosoftAuthProvider MicrosoftAuthProvider,
        AddAccountUseCase AddAccountUseCase)
    {
        this.MicrosoftAuthProvider = MicrosoftAuthProvider ?? throw new ArgumentNullException(nameof(MicrosoftAuthProvider));
        this.AddAccountUseCase = AddAccountUseCase ?? throw new ArgumentNullException(nameof(AddAccountUseCase));
    }

    public async Task<Result<LauncherAccount>> ExecuteAsync(bool SetAsDefault = true, CancellationToken CancellationToken = default)
    {
        var AuthResult = await MicrosoftAuthProvider.SignInAsync(CancellationToken).ConfigureAwait(false);
        if (AuthResult.IsFailure)
        {
            return Result<LauncherAccount>.Failure(AuthResult.Error);
        }

        if (string.IsNullOrWhiteSpace(AuthResult.Value.Username) ||
            string.IsNullOrWhiteSpace(AuthResult.Value.AccountIdentifier) ||
            string.IsNullOrWhiteSpace(AuthResult.Value.RefreshToken))
        {
            return Result<LauncherAccount>.Failure(AccountErrors.InvalidRequest);
        }

        return await AddAccountUseCase.ExecuteAsync(
            new AddAccountRequest
            {
                Provider = AccountProvider.Microsoft,
                Username = AuthResult.Value.Username,
                AccountIdentifier = AuthResult.Value.AccountIdentifier,
                RefreshToken = AuthResult.Value.RefreshToken,
                SetAsDefault = SetAsDefault
            },
            CancellationToken).ConfigureAwait(false);
    }
}