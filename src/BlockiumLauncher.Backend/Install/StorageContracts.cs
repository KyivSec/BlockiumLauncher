using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Shared.Primitives;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Storage;

public interface IArchiveExtractor
{
    Task<Result<Unit>> ExtractAsync(string ArchivePath, string DestinationPath, CancellationToken CancellationToken = default);
}

public interface IFileTransaction : IAsyncDisposable
{
    string? TargetRootPath { get; }

    Task<Result<Unit>> BeginAsync(string TargetRootPath, CancellationToken CancellationToken = default);
    Task<Result<Unit>> StageDirectoryAsync(string SourceDirectoryPath, CancellationToken CancellationToken = default);
    Task<Result<Unit>> CommitAsync(CancellationToken CancellationToken = default);
    Task<Result<Unit>> RollbackAsync(CancellationToken CancellationToken = default);
}

public interface IInstanceContentInstaller
{
    Task<Result<string>> PrepareAsync(
        InstallPlan Plan,
        ITempWorkspace Workspace,
        CancellationToken CancellationToken = default);
}

public interface ITempWorkspace : IAsyncDisposable
{
    string RootPath { get; }
    string GetPath(string RelativePath);
    Task CreateDirectoryAsync(string RelativePath, CancellationToken CancellationToken = default);
}

public interface ITempWorkspaceFactory
{
    Task<ITempWorkspace> CreateAsync(string OperationName, CancellationToken CancellationToken = default);
}
