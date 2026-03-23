using System.Linq;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Storage;

public sealed class InstanceContentInstaller : IInstanceContentInstaller
{
    private readonly IReadOnlyList<ILoaderRuntimePreparer> LoaderRuntimePreparers;

    public InstanceContentInstaller(IEnumerable<ILoaderRuntimePreparer> loaderRuntimePreparers)
    {
        LoaderRuntimePreparers = (loaderRuntimePreparers ?? throw new ArgumentNullException(nameof(loaderRuntimePreparers))).ToArray();
    }

    public Task<Result<string>> PrepareAsync(
        InstallPlan plan,
        ITempWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(workspace);

        var preparer = LoaderRuntimePreparers.FirstOrDefault(candidate => candidate.CanPrepare(plan.LoaderType));
        if (preparer is null)
        {
            return Task.FromResult(Result<string>.Failure(
                new Error(
                    "Install.LoaderRuntimePreparerNotFound",
                    $"No loader runtime preparer is registered for loader type '{plan.LoaderType}'.")));
        }

        return preparer.PrepareAsync(plan, workspace, cancellationToken);
    }
}