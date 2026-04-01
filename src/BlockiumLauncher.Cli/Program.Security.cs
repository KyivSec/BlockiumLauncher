using BlockiumLauncher.Application.UseCases.Catalog;
using Microsoft.Extensions.DependencyInjection;

namespace BlockiumLauncher.Cli;

internal static partial class Program
{
    private static Task<int> HandleCatalogKeySetAsync(IServiceProvider serviceProvider, string[] args, bool outputJson)
    {
        var apiKey = GetOptionalOption(args, "--api-key");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = ReadSecretFromConsole("CurseForge API key");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            WriteFailure("Cli.InvalidArguments", "A CurseForge API key is required.", outputJson);
            return Task.FromResult(CliExitCodes.InvalidArguments);
        }

        var useCase = serviceProvider.GetRequiredService<ConfigureCurseForgeApiKeyUseCase>();
        var result = useCase.Execute(new ConfigureCurseForgeApiKeyRequest
        {
            ApiKey = apiKey
        });

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return Task.FromResult(CliExitCodes.OperationFailed);
        }

        var status = serviceProvider.GetRequiredService<GetCurseForgeApiKeyStatusUseCase>().Execute();
        WriteSuccess(status, outputJson, lines =>
        {
            lines.Add("Stored the CurseForge API key in the secure launcher store.");
            lines.Add($"Backend: {status.BackendName}");
            lines.Add($"EffectiveSource: {status.EffectiveSource}");
        });

        return Task.FromResult(CliExitCodes.Success);
    }

    private static Task<int> HandleCatalogKeyClearAsync(IServiceProvider serviceProvider, bool outputJson)
    {
        var useCase = serviceProvider.GetRequiredService<ClearCurseForgeApiKeyUseCase>();
        var result = useCase.Execute();

        if (result.IsFailure)
        {
            WriteFailure(result.Error.Code, result.Error.Message, outputJson);
            return Task.FromResult(CliExitCodes.OperationFailed);
        }

        var status = serviceProvider.GetRequiredService<GetCurseForgeApiKeyStatusUseCase>().Execute();
        WriteSuccess(status, outputJson, lines =>
        {
            lines.Add("Cleared the stored CurseForge API key from the secure launcher store.");
            lines.Add($"Backend: {status.BackendName}");
            lines.Add($"EffectiveSource: {status.EffectiveSource}");
        });

        return Task.FromResult(CliExitCodes.Success);
    }

    private static Task<int> HandleCatalogKeyStatusAsync(IServiceProvider serviceProvider, bool outputJson)
    {
        var status = serviceProvider.GetRequiredService<GetCurseForgeApiKeyStatusUseCase>().Execute();

        WriteSuccess(status, outputJson, lines =>
        {
            lines.Add($"Backend: {status.BackendName}");
            lines.Add($"CanPersistSecrets: {status.CanPersistSecrets}");
            lines.Add($"EnvironmentVariablePresent: {status.EnvironmentVariablePresent}");
            lines.Add($"SecureStoreValuePresent: {status.SecureStoreValuePresent}");
            lines.Add($"EffectiveSource: {status.EffectiveSource}");
        });

        return Task.FromResult(CliExitCodes.Success);
    }

    private static string ReadSecretFromConsole(string label)
    {
        Console.Write(label + ": ");

        var buffer = new List<char>();
        while (true)
        {
            var keyInfo = Console.ReadKey(intercept: true);
            if (keyInfo.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }

            if (keyInfo.Key == ConsoleKey.Backspace)
            {
                if (buffer.Count > 0)
                {
                    buffer.RemoveAt(buffer.Count - 1);
                }

                continue;
            }

            if (!char.IsControl(keyInfo.KeyChar))
            {
                buffer.Add(keyInfo.KeyChar);
            }
        }

        return new string(buffer.ToArray());
    }
}
