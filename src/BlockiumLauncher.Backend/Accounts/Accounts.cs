using System.Security.Cryptography;
using System.Text;
using BlockiumLauncher.Application.Abstractions.Auth;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Security;
using BlockiumLauncher.Contracts.Accounts;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Accounts
{
    public static class AccountErrors
    {
        public static readonly Error InvalidRequest = new("Account.InvalidRequest", "The account request is invalid.");
        public static readonly Error AccountNotFound = new("Account.NotFound", "The requested account was not found.");
        public static readonly Error TokenMissing = new("Account.TokenMissing", "The requested account token was not found.");
        public static readonly Error PersistenceFailed = new("Account.PersistenceFailed", "Failed to persist account data.");
        public static readonly Error MicrosoftAuthNotConfigured = new("Account.MicrosoftAuthNotConfigured", "Microsoft account sign-in is not configured for this launcher build.");
        public static readonly Error NoDefaultAccount = new("Account.NoDefaultAccount", "No default account is configured.");
        public static readonly Error NoOfflineAccount = new("Account.NoOfflineAccount", "No offline account is available.");
        public static readonly Error AccountNotUsableForOfflineLaunch = new("Account.AccountNotUsableForOfflineLaunch", "The selected account cannot be used for offline launch.");
    }

    public sealed class AddAccountRequest
    {
        public AccountProvider Provider { get; init; }
        public string Username { get; init; } = string.Empty;
        public string? AccountIdentifier { get; init; }
        public string? RefreshToken { get; init; }
        public bool SetAsDefault { get; init; }
    }

    public sealed class AddOfflineAccountRequest
    {
        public string DisplayName { get; }
        public bool SetAsDefault { get; }

        public AddOfflineAccountRequest(string DisplayName, bool SetAsDefault = true)
        {
            this.DisplayName = NormalizeRequired(DisplayName, nameof(DisplayName));
            this.SetAsDefault = SetAsDefault;
        }

        private static string NormalizeRequired(string Value, string ParamName)
        {
            if (string.IsNullOrWhiteSpace(Value))
            {
                throw new ArgumentException("Value cannot be null or whitespace.", ParamName);
            }

            return Value.Trim();
        }
    }

    public sealed class RefreshAccountRequest
    {
        public AccountId AccountId { get; }

        public RefreshAccountRequest(AccountId AccountId)
        {
            this.AccountId = AccountId;
        }
    }

    public sealed class RemoveAccountRequest
    {
        public AccountId AccountId { get; init; }
    }

    public sealed class ResolveOfflineLaunchAccountRequest
    {
        public AccountId? AccountId { get; init; }
    }

    public sealed class SetDefaultAccountRequest
    {
        public AccountId AccountId { get; init; }
    }

    public sealed class AddAccountUseCase
    {
        private readonly IAccountRepository AccountRepository;
        private readonly ITokenStore TokenStore;

        public AddAccountUseCase(
            IAccountRepository AccountRepository,
            ITokenStore TokenStore)
        {
            this.AccountRepository = AccountRepository ?? throw new ArgumentNullException(nameof(AccountRepository));
            this.TokenStore = TokenStore ?? throw new ArgumentNullException(nameof(TokenStore));
        }

        public async Task<Result<LauncherAccount>> ExecuteAsync(
            AddAccountRequest Request,
            CancellationToken CancellationToken = default)
        {
            if (Request is null || string.IsNullOrWhiteSpace(Request.Username))
            {
                return Result<LauncherAccount>.Failure(AccountErrors.InvalidRequest);
            }

            var ExistingAccounts = await AccountRepository.ListAsync(CancellationToken).ConfigureAwait(false);
            var ShouldBeDefault = Request.SetAsDefault || ExistingAccounts.Count == 0;
            var NewAccountId = AccountId.New();

            if (ShouldBeDefault)
            {
                foreach (var ExistingAccount in ExistingAccounts)
                {
                    if (ExistingAccount.IsDefault)
                    {
                        ExistingAccount.ClearDefault();
                        await AccountRepository.SaveAsync(ExistingAccount, CancellationToken).ConfigureAwait(false);
                    }
                }
            }

            var RefreshTokenRef = string.IsNullOrWhiteSpace(Request.RefreshToken)
                ? null
                : "account-refresh:" + NewAccountId;

            LauncherAccount Account;
            if (Request.Provider == AccountProvider.Microsoft)
            {
                if (string.IsNullOrWhiteSpace(Request.AccountIdentifier))
                {
                    return Result<LauncherAccount>.Failure(AccountErrors.InvalidRequest);
                }

                Account = LauncherAccount.CreateMicrosoft(
                    NewAccountId,
                    Request.Username,
                    Request.AccountIdentifier,
                    RefreshTokenRef,
                    ShouldBeDefault);
            }
            else
            {
                Account = LauncherAccount.CreateOffline(
                    NewAccountId,
                    Request.Username,
                    null,
                    ShouldBeDefault);
            }

            await AccountRepository.SaveAsync(Account, CancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(Request.RefreshToken))
            {
                var TokenResult = await TokenStore.SaveRefreshTokenAsync(
                    Account.AccountId,
                    Request.RefreshToken,
                    CancellationToken).ConfigureAwait(false);

                if (TokenResult.IsFailure)
                {
                    return Result<LauncherAccount>.Failure(TokenResult.Error);
                }
            }

            return Result<LauncherAccount>.Success(Account);
        }
    }

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

    public sealed class RemoveAccountUseCase
    {
        private readonly IAccountRepository AccountRepository;
        private readonly ITokenStore TokenStore;

        public RemoveAccountUseCase(
            IAccountRepository AccountRepository,
            ITokenStore TokenStore)
        {
            this.AccountRepository = AccountRepository ?? throw new ArgumentNullException(nameof(AccountRepository));
            this.TokenStore = TokenStore ?? throw new ArgumentNullException(nameof(TokenStore));
        }

        public async Task<Result> ExecuteAsync(
            RemoveAccountRequest Request,
            CancellationToken CancellationToken = default)
        {
            if (Request is null)
            {
                return Result.Failure(AccountErrors.InvalidRequest);
            }

            var Accounts = await AccountRepository.ListAsync(CancellationToken).ConfigureAwait(false);
            var Target = Accounts.FirstOrDefault(x => x.AccountId == Request.AccountId);

            if (Target is null)
            {
                return Result.Failure(AccountErrors.AccountNotFound);
            }

            await AccountRepository.DeleteAsync(Request.AccountId, CancellationToken).ConfigureAwait(false);
            await TokenStore.DeleteRefreshTokenAsync(Request.AccountId, CancellationToken).ConfigureAwait(false);

            if (Target.IsDefault)
            {
                var RemainingAccounts = await AccountRepository.ListAsync(CancellationToken).ConfigureAwait(false);
                var Replacement = RemainingAccounts.FirstOrDefault();

                if (Replacement is not null && !Replacement.IsDefault)
                {
                    Replacement.SetDefault();
                    await AccountRepository.SaveAsync(Replacement, CancellationToken).ConfigureAwait(false);
                }
            }

            return Result.Success();
        }
    }

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

    public sealed class SetDefaultAccountUseCase
    {
        private readonly IAccountRepository AccountRepository;

        public SetDefaultAccountUseCase(IAccountRepository AccountRepository)
        {
            this.AccountRepository = AccountRepository ?? throw new ArgumentNullException(nameof(AccountRepository));
        }

        public async Task<Result> ExecuteAsync(
            SetDefaultAccountRequest Request,
            CancellationToken CancellationToken = default)
        {
            if (Request is null)
            {
                return Result.Failure(AccountErrors.InvalidRequest);
            }

            var Accounts = await AccountRepository.ListAsync(CancellationToken).ConfigureAwait(false);
            var Target = Accounts.FirstOrDefault(x => x.AccountId == Request.AccountId);

            if (Target is null)
            {
                return Result.Failure(AccountErrors.AccountNotFound);
            }

            foreach (var Account in Accounts)
            {
                if (Account.AccountId == Request.AccountId)
                {
                    if (!Account.IsDefault)
                    {
                        Account.SetDefault();
                        await AccountRepository.SaveAsync(Account, CancellationToken).ConfigureAwait(false);
                    }
                }
                else if (Account.IsDefault)
                {
                    Account.ClearDefault();
                    await AccountRepository.SaveAsync(Account, CancellationToken).ConfigureAwait(false);
                }
            }

            return Result.Success();
        }
    }

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
}
