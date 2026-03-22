using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Accounts;

public sealed class ListAccountsUseCase
{
    private readonly IAccountRepository AccountRepository;

    public ListAccountsUseCase(IAccountRepository AccountRepository)
    {
        this.AccountRepository = AccountRepository ?? throw new ArgumentNullException(nameof(AccountRepository));
    }

    public async Task<Result<IReadOnlyList<LauncherAccount>>> ExecuteAsync(CancellationToken CancellationToken = default)
    {
        var Accounts = await AccountRepository.ListAsync(CancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<LauncherAccount>>.Success(Accounts);
    }
}