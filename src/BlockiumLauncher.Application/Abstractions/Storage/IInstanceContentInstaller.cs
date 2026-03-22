using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Storage;

public interface IInstanceContentInstaller
{
    Task<Result<string>> PrepareAsync(
        InstallPlan Plan,
        ITempWorkspace Workspace,
        CancellationToken CancellationToken = default);
}