using BlockiumLauncher.Application.UseCases.Accounts;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace BlockiumLauncher.Cli;

internal static partial class Program
{
    private static async Task<int> HandleAccountsListAsync(IServiceProvider serviceProvider, bool outputJson)
    {
        var useCase = serviceProvider.GetRequiredService<ListAccountsUseCase>();
        var result = await useCase.ExecuteAsync().ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        var payload = result.Value.Select(account => new
        {
            AccountId = account.AccountId.ToString(),
            account.Provider,
            account.Username,
            account.AccountIdentifier,
            account.IsDefault,
            account.State
        }).ToArray();

        WriteSuccess(payload, outputJson, lines =>
        {
            if (payload.Length == 0)
            {
                lines.Add("No accounts found.");
                return;
            }

            foreach (var account in payload)
            {
                lines.Add($"{account.AccountId} | {account.Provider} | {account.Username} | Default={account.IsDefault} | State={account.State}");
            }
        });

        return CliExitCodes.Success;
    }

    private static async Task<int> HandleAccountsAddOfflineAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var username = GetRequiredOption(args, "--username");
        if (string.IsNullOrWhiteSpace(username))
        {
            WriteFailure("Cli.InvalidArguments", "Missing required option --username.", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        var setAsDefault = HasFlag(args, "--set-default");

        var useCase = serviceProvider.GetRequiredService<AddAccountUseCase>();
        var result = await useCase.ExecuteAsync(new AddAccountRequest
        {
            Provider = AccountProvider.Offline,
            Username = username,
            SetAsDefault = setAsDefault
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        var payload = new
        {
            AccountId = result.Value.AccountId.ToString(),
            result.Value.Provider,
            result.Value.Username,
            result.Value.IsDefault,
            result.Value.State
        };

        WriteSuccess(payload, outputJson, lines =>
        {
            lines.Add("Offline account added.");
            lines.Add($"{payload.AccountId} | {payload.Username} | Default={payload.IsDefault} | State={payload.State}");
        });

        return CliExitCodes.Success;
    }

    private static async Task<int> HandleAccountsSetDefaultAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var accountIdText = GetRequiredOption(args, "--account-id");
        if (string.IsNullOrWhiteSpace(accountIdText))
        {
            WriteFailure("Cli.InvalidArguments", "Missing required option --account-id.", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        var useCase = serviceProvider.GetRequiredService<SetDefaultAccountUseCase>();
        var result = await useCase.ExecuteAsync(new SetDefaultAccountRequest
        {
            AccountId = new AccountId(accountIdText)
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        WriteSuccess(new { AccountId = accountIdText }, outputJson, lines =>
        {
            lines.Add($"Default account set: {accountIdText}");
        });

        return CliExitCodes.Success;
    }

    private static async Task<int> HandleAccountsRemoveAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var accountIdText = GetRequiredOption(args, "--account-id");
        if (string.IsNullOrWhiteSpace(accountIdText))
        {
            WriteFailure("Cli.InvalidArguments", "Missing required option --account-id.", outputJson);
            return CliExitCodes.InvalidArguments;
        }

        var useCase = serviceProvider.GetRequiredService<RemoveAccountUseCase>();
        var result = await useCase.ExecuteAsync(new RemoveAccountRequest
        {
            AccountId = new AccountId(accountIdText)
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return CliExitCodes.OperationFailed;
        }

        WriteSuccess(new { AccountId = accountIdText }, outputJson, lines =>
        {
            lines.Add($"Account removed: {accountIdText}");
        });

        return CliExitCodes.Success;
    }
}
