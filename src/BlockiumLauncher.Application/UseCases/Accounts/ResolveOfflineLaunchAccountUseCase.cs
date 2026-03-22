using System.Security.Cryptography;
using System.Text;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Contracts.Accounts;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Accounts;

public sealed class ResolveOfflineLaunchAccountUseCase
{
    private readonly IAccountRepository AccountRepository;

    public ResolveOfflineLaunchAccountUseCase(IAccountRepository AccountRepository)
    {
        this.AccountRepository = AccountRepository ?? throw new ArgumentNullException(nameof(AccountRepository));
    }

    public async Task<Result<LaunchAccountContextDto>> ExecuteAsync(
        ResolveOfflineLaunchAccountRequest? Request = null,
        CancellationToken CancellationToken = default)
    {
        LauncherAccount? Account;

        if (Request?.AccountId is not null)
        {
            Account = await AccountRepository.GetByIdAsync(Request.AccountId.Value, CancellationToken).ConfigureAwait(false);
            if (Account is null)
            {
                return Result<LaunchAccountContextDto>.Failure(AccountErrors.AccountNotFound);
            }
        }
        else
        {
            Account = await AccountRepository.GetDefaultAsync(CancellationToken).ConfigureAwait(false);

            if (Account is null)
            {
                var Accounts = await AccountRepository.ListAsync(CancellationToken).ConfigureAwait(false);
                var OfflineAccounts = Accounts
                    .Where(IsUsableOfflineAccount)
                    .ToList();

                if (OfflineAccounts.Count == 1)
                {
                    Account = OfflineAccounts[0];
                }
                else
                {
                    return Result<LaunchAccountContextDto>.Failure(AccountErrors.NoOfflineAccount);
                }
            }
        }

        if (!IsUsableOfflineAccount(Account))
        {
            return Result<LaunchAccountContextDto>.Failure(AccountErrors.AccountNotUsableForOfflineLaunch);
        }

        return Result<LaunchAccountContextDto>.Success(new LaunchAccountContextDto
        {
            AccountId = Account.AccountId.ToString(),
            Username = Account.Username,
            PlayerUuid = CreateOfflinePlayerUuid(Account.Username),
            IsOffline = true
        });
    }

    private static bool IsUsableOfflineAccount(LauncherAccount? Account)
    {
        return Account is not null
            && Account.Provider == AccountProvider.Offline
            && Account.State != AccountState.Removed
            && !string.IsNullOrWhiteSpace(Account.Username);
    }

    public static string CreateOfflinePlayerUuid(string Username)
    {
        var Bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + Username));

        Bytes[6] = (byte)((Bytes[6] & 0x0F) | 0x30);
        Bytes[8] = (byte)((Bytes[8] & 0x3F) | 0x80);

        return new Guid(Bytes).ToString("D");
    }
}