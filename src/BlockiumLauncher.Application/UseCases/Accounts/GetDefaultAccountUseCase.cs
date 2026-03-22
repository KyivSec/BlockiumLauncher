using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Accounts;

public sealed class GetDefaultAccountUseCase
{
    private readonly IAccountRepository AccountRepository;

    public GetDefaultAccountUseCase(IAccountRepository AccountRepository)
    {
        this.AccountRepository = AccountRepository ?? throw new ArgumentNullException(nameof(AccountRepository));
    }

    public async Task<Result<LauncherAccount>> ExecuteAsync(CancellationToken CancellationToken = default)
    {
        var Account = await AccountRepository.GetDefaultAsync(CancellationToken).ConfigureAwait(false);
        if (Account is null)
        {
            return Result<LauncherAccount>.Failure(AccountErrors.NoDefaultAccount);
        }

        return Result<LauncherAccount>.Success(Account);
    }
}