using System.Threading;
using System.Threading.Tasks;

namespace BlockiumLauncher.Application.Abstractions.Storage;

public interface ITempWorkspaceFactory
{
    Task<ITempWorkspace> CreateAsync(string OperationName, CancellationToken CancellationToken = default);
}