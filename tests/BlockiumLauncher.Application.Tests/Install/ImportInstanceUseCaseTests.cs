using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Paths;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.Diagnostics;
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
            new FakeInstanceRepository(),
            new FakeLauncherPaths(),
            NoOpInstanceContentMetadataService.Instance);

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

    private sealed class FakeLauncherPaths : ILauncherPaths
    {
        public string RootDirectory => Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests.Root");
        public string DataDirectory => Path.Combine(RootDirectory, "data");
        public string CacheDirectory => Path.Combine(RootDirectory, "cache");
        public string InstancesDirectory => Path.Combine(RootDirectory, "instances");
        public string SharedDirectory => Path.Combine(RootDirectory, "shared");
        public string SharedVersionsDirectory => Path.Combine(SharedDirectory, "versions");
        public string SharedLibrariesDirectory => Path.Combine(SharedDirectory, "libraries");
        public string SharedAssetsDirectory => Path.Combine(SharedDirectory, "assets");
        public string SharedAssetIndexesDirectory => Path.Combine(SharedAssetsDirectory, "indexes");
        public string SharedAssetObjectsDirectory => Path.Combine(SharedAssetsDirectory, "objects");
        public string SharedLoadersDirectory => Path.Combine(SharedDirectory, "loaders");
        public string SharedNativesDirectory => Path.Combine(SharedDirectory, "natives");
        public string LogsDirectory => Path.Combine(RootDirectory, "logs");
        public string DiagnosticsDirectory => Path.Combine(RootDirectory, "diagnostics");
        public string LatestLogFilePath => Path.Combine(LogsDirectory, "latest.log");
        public string RuntimesDirectory => Path.Combine(RootDirectory, "runtimes");
        public string ManagedJavaDirectory => Path.Combine(RuntimesDirectory, "java");
        public string InstancesFilePath => Path.Combine(DataDirectory, "instances.json");
        public string AccountsFilePath => Path.Combine(DataDirectory, "accounts.json");
        public string JavaInstallationsFilePath => Path.Combine(DataDirectory, "java-installations.json");
        public string VersionsCacheFilePath => Path.Combine(CacheDirectory, "versions.json");

        public string GetLoaderVersionsCacheFilePath(BlockiumLauncher.Domain.Enums.LoaderType loaderType, VersionId gameVersion) => Path.Combine(CacheDirectory, "loaders", $"{loaderType}-{gameVersion}.json");
        public string GetSharedVersionDirectory(string gameVersion) => Path.Combine(SharedVersionsDirectory, gameVersion);
        public string GetSharedVersionJsonPath(string gameVersion) => Path.Combine(GetSharedVersionDirectory(gameVersion), $"{gameVersion}.json");
        public string GetSharedClientJarPath(string gameVersion) => Path.Combine(GetSharedVersionDirectory(gameVersion), $"{gameVersion}.jar");
        public string GetSharedNativesDirectory(string runtimeKey) => Path.Combine(SharedNativesDirectory, runtimeKey);
        public string GetSharedLoaderDirectory(BlockiumLauncher.Domain.Enums.LoaderType loaderType, string gameVersion, string loaderVersion) => Path.Combine(SharedLoadersDirectory, loaderType.ToString(), gameVersion, loaderVersion);
        public string GetManagedJavaDirectory(string runtimeKey) => Path.Combine(ManagedJavaDirectory, runtimeKey);
        public string GetDefaultInstanceDirectory(string instanceName) => Path.Combine(InstancesDirectory, instanceName);
        public string GetInstanceDataDirectory(string installLocation) => Path.Combine(installLocation, ".blockium");
        public string GetInstanceMetadataFilePath(string installLocation) => Path.Combine(GetInstanceDataDirectory(installLocation), "instance-metadata.json");
        public string GetContextLogFilePath(string context, DateTimeOffset? timestampUtc = null) => Path.Combine(LogsDirectory, $"{context}_{(timestampUtc ?? DateTimeOffset.UtcNow):yyyyMMdd}.log");
    }
}
