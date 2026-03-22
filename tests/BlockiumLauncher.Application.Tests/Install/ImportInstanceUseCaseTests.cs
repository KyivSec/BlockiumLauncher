using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.ValueObjects;
using Xunit;

namespace BlockiumLauncher.Application.Tests.Install;

public sealed class ImportInstanceUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenSourceDirectoryIsMissing()
    {
        var UseCase = new ImportInstanceUseCase(
            new FakeTempWorkspaceFactory(),
            new FakeFileTransaction(),
            new FakeInstanceRepository());

        var Result = await UseCase.ExecuteAsync(new ImportInstanceRequest
        {
            InstanceName = "Imported",
            SourceDirectory = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Path.GetRandomFileName())
        });

        Assert.True(Result.IsFailure);
        Assert.Equal("Install.ImportSourceMissing", Result.Error.Code);
    }

    private sealed class FakeTempWorkspaceFactory : ITempWorkspaceFactory
    {
        public Task<ITempWorkspace> CreateAsync(string OperationName, CancellationToken CancellationToken = default)
            => Task.FromResult<ITempWorkspace>(new FakeTempWorkspace());
    }

    private sealed class FakeTempWorkspace : ITempWorkspace
    {
        public string RootPath { get; } = Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests", Path.GetRandomFileName());

        public FakeTempWorkspace()
        {
            Directory.CreateDirectory(RootPath);
        }

        public Task CreateDirectoryAsync(string RelativePath, CancellationToken CancellationToken = default)
        {
            Directory.CreateDirectory(GetPath(RelativePath));
            return Task.CompletedTask;
        }

        public string GetPath(string RelativePath) => Path.Combine(RootPath, RelativePath);

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeFileTransaction : IFileTransaction
    {
        public string? TargetRootPath { get; private set; }

        public Task<BlockiumLauncher.Shared.Results.Result<BlockiumLauncher.Shared.Primitives.Unit>> BeginAsync(string TargetRootPath, CancellationToken CancellationToken = default)
        {
            this.TargetRootPath = TargetRootPath;
            return Task.FromResult(BlockiumLauncher.Shared.Results.Result<BlockiumLauncher.Shared.Primitives.Unit>.Success(BlockiumLauncher.Shared.Primitives.Unit.Value));
        }

        public Task<BlockiumLauncher.Shared.Results.Result<BlockiumLauncher.Shared.Primitives.Unit>> StageDirectoryAsync(string SourceDirectoryPath, CancellationToken CancellationToken = default)
            => Task.FromResult(BlockiumLauncher.Shared.Results.Result<BlockiumLauncher.Shared.Primitives.Unit>.Success(BlockiumLauncher.Shared.Primitives.Unit.Value));

        public Task<BlockiumLauncher.Shared.Results.Result<BlockiumLauncher.Shared.Primitives.Unit>> CommitAsync(CancellationToken CancellationToken = default)
            => Task.FromResult(BlockiumLauncher.Shared.Results.Result<BlockiumLauncher.Shared.Primitives.Unit>.Success(BlockiumLauncher.Shared.Primitives.Unit.Value));

        public Task<BlockiumLauncher.Shared.Results.Result<BlockiumLauncher.Shared.Primitives.Unit>> RollbackAsync(CancellationToken CancellationToken = default)
            => Task.FromResult(BlockiumLauncher.Shared.Results.Result<BlockiumLauncher.Shared.Primitives.Unit>.Success(BlockiumLauncher.Shared.Primitives.Unit.Value));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeInstanceRepository : IInstanceRepository
    {
        public Task<IReadOnlyList<LauncherInstance>> ListAsync(CancellationToken CancellationToken = default)
            => Task.FromResult<IReadOnlyList<LauncherInstance>>([]);

        public Task<LauncherInstance?> GetByIdAsync(InstanceId InstanceId, CancellationToken CancellationToken = default)
            => Task.FromResult<LauncherInstance?>(null);

        public Task<LauncherInstance?> GetByNameAsync(string Name, CancellationToken CancellationToken = default)
            => Task.FromResult<LauncherInstance?>(null);

        public Task SaveAsync(LauncherInstance Instance, CancellationToken CancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(InstanceId InstanceId, CancellationToken CancellationToken = default)
            => Task.CompletedTask;
    }
}