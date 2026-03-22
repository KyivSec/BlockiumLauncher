using System;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Shared.Primitives;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Storage;

public interface IFileTransaction : IAsyncDisposable
{
    string? TargetRootPath { get; }

    Task<Result<Unit>> BeginAsync(string TargetRootPath, CancellationToken CancellationToken = default);

    Task<Result<Unit>> StageDirectoryAsync(string SourceDirectoryPath, CancellationToken CancellationToken = default);

    Task<Result<Unit>> CommitAsync(CancellationToken CancellationToken = default);

    Task<Result<Unit>> RollbackAsync(CancellationToken CancellationToken = default);
}