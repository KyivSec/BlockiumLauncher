using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Storage;

namespace BlockiumLauncher.Infrastructure.Storage;

public sealed class TempWorkspaceFactory : ITempWorkspaceFactory
{
    public Task<ITempWorkspace> CreateAsync(string OperationName, CancellationToken CancellationToken = default)
    {
        CancellationToken.ThrowIfCancellationRequested();

        var SafeOperationName = string.IsNullOrWhiteSpace(OperationName)
            ? "operation"
            : OperationName.Trim().Replace(' ', '-').ToLowerInvariant();

        var RootPath = Path.Combine(
            Path.GetTempPath(),
            "BlockiumLauncher",
            "stage8",
            $"{SafeOperationName}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}");

        Directory.CreateDirectory(RootPath);

        ITempWorkspace Workspace = new TempWorkspace(RootPath);
        return Task.FromResult(Workspace);
    }
}