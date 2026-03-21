using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.Abstractions.Repositories;

public interface IInstanceRepository
{
    Task<IReadOnlyList<LauncherInstance>> ListAsync(CancellationToken CancellationToken);
    Task<LauncherInstance?> GetByIdAsync(InstanceId InstanceId, CancellationToken CancellationToken);
    Task<LauncherInstance?> GetByNameAsync(string Name, CancellationToken CancellationToken);
    Task SaveAsync(LauncherInstance Instance, CancellationToken CancellationToken);
    Task DeleteAsync(InstanceId InstanceId, CancellationToken CancellationToken);
}
