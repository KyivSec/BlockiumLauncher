using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;
using Xunit;

namespace BlockiumLauncher.Application.Tests.Install;

public sealed class InstallInstanceUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_SavesInstance_OnSuccessfulInstall()
    {
        var Repository = new FakeInstanceRepository();
        var Builder = new InstallPlanBuilder(new FakeVersionManifestService(), new FakeLoaderMetadataService());
        var UseCase = new InstallInstanceUseCase(
            Builder,
            new FakeTempWorkspaceFactory(),
            new FakeInstanceContentInstaller(),
            new FakeFileTransaction(),
            Repository);

        var Result = await UseCase.ExecuteAsync(new InstallInstanceRequest
        {
            InstanceName = "InstalledInstance",
            GameVersion = "1.20.1",
            LoaderType = LoaderType.Vanilla
        });

        Assert.True(Result.IsSuccess);
        Assert.NotNull(Repository.SavedInstance);
        Assert.Equal("InstalledInstance", Repository.SavedInstance!.Name);
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

    private sealed class FakeInstanceContentInstaller : IInstanceContentInstaller
    {
        public Task<Result<string>> PrepareAsync(InstallPlan Plan, ITempWorkspace Workspace, CancellationToken CancellationToken = default)
        {
            var Root = Workspace.GetPath("prepared");
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "instance.json"), "{}");
            return Task.FromResult(Result<string>.Success(Root));
        }
    }

    private sealed class FakeFileTransaction : IFileTransaction
    {
        public string? TargetRootPath { get; private set; }

        public Task<Result<BlockiumLauncher.Shared.Primitives.Unit>> BeginAsync(string TargetRootPath, CancellationToken CancellationToken = default)
        {
            this.TargetRootPath = TargetRootPath;
            return Task.FromResult(Result<BlockiumLauncher.Shared.Primitives.Unit>.Success(BlockiumLauncher.Shared.Primitives.Unit.Value));
        }

        public Task<Result<BlockiumLauncher.Shared.Primitives.Unit>> StageDirectoryAsync(string SourceDirectoryPath, CancellationToken CancellationToken = default)
            => Task.FromResult(Result<BlockiumLauncher.Shared.Primitives.Unit>.Success(BlockiumLauncher.Shared.Primitives.Unit.Value));

        public Task<Result<BlockiumLauncher.Shared.Primitives.Unit>> CommitAsync(CancellationToken CancellationToken = default)
            => Task.FromResult(Result<BlockiumLauncher.Shared.Primitives.Unit>.Success(BlockiumLauncher.Shared.Primitives.Unit.Value));

        public Task<Result<BlockiumLauncher.Shared.Primitives.Unit>> RollbackAsync(CancellationToken CancellationToken = default)
            => Task.FromResult(Result<BlockiumLauncher.Shared.Primitives.Unit>.Success(BlockiumLauncher.Shared.Primitives.Unit.Value));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeInstanceRepository : IInstanceRepository
    {
        public LauncherInstance? SavedInstance { get; private set; }

        public Task<IReadOnlyList<LauncherInstance>> ListAsync(CancellationToken CancellationToken = default)
            => Task.FromResult<IReadOnlyList<LauncherInstance>>([]);

        public Task<LauncherInstance?> GetByIdAsync(InstanceId InstanceId, CancellationToken CancellationToken = default)
            => Task.FromResult<LauncherInstance?>(null);

        public Task<LauncherInstance?> GetByNameAsync(string Name, CancellationToken CancellationToken = default)
            => Task.FromResult<LauncherInstance?>(null);

        public Task SaveAsync(LauncherInstance Instance, CancellationToken CancellationToken = default)
        {
            SavedInstance = Instance;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(InstanceId InstanceId, CancellationToken CancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeVersionManifestService : IVersionManifestService
    {
        public Task<Result<IReadOnlyList<VersionSummary>>> GetAvailableVersionsAsync(CancellationToken CancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<VersionSummary>>.Success(
            [
                new VersionSummary(CreateVersionId("1.20.1"), "1.20.1", true, DateTimeOffset.UtcNow)
            ]));

        public Task<Result<VersionSummary?>> GetVersionAsync(VersionId VersionId, CancellationToken CancellationToken = default)
            => Task.FromResult(Result<VersionSummary?>.Success(
                new VersionSummary(VersionId, VersionId.ToString(), true, DateTimeOffset.UtcNow)));
    }

    private sealed class FakeLoaderMetadataService : ILoaderMetadataService
    {
        public Task<Result<IReadOnlyList<LoaderVersionSummary>>> GetLoaderVersionsAsync(
            LoaderType LoaderType,
            VersionId VersionId,
            CancellationToken CancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<LoaderVersionSummary>>.Success(
            [
                new LoaderVersionSummary(LoaderType, VersionId, "0.15.0", true)
            ]));
    }

    private static VersionId CreateVersionId(string Value)
    {
        var Type = typeof(VersionId);

        var ParseMethod = Type.GetMethod("Parse", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(string) }, null);
        if (ParseMethod is not null)
        {
            return (VersionId)ParseMethod.Invoke(null, [Value])!;
        }

        var CreateMethod = Type.GetMethod("Create", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(string) }, null);
        if (CreateMethod is not null)
        {
            return (VersionId)CreateMethod.Invoke(null, [Value])!;
        }

        var Constructor = Type.GetConstructor([typeof(string)]);
        if (Constructor is not null)
        {
            return (VersionId)Constructor.Invoke([Value]);
        }

        throw new InvalidOperationException("Could not create VersionId.");
    }
}