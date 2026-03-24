using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Paths;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.Diagnostics;
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
        var Builder = new InstallPlanBuilder(new FakeVersionManifestService(), new FakeLoaderMetadataService(), new FakeLauncherPaths());
        var UseCase = new InstallInstanceUseCase(
            Builder,
            new FakeTempWorkspaceFactory(),
            new FakeInstanceContentInstaller(),
            new FakeFileTransaction(),
            Repository,
            NoOpInstanceContentMetadataService.Instance);

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

    private sealed class FakeLauncherPaths : ILauncherPaths
    {
        public string RootDirectory => @"C:\LauncherRoot";
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

        public string GetLoaderVersionsCacheFilePath(LoaderType loaderType, VersionId gameVersion) => Path.Combine(CacheDirectory, "loaders", $"{loaderType.ToString().ToLowerInvariant()}-{gameVersion}.json");
        public string GetSharedVersionDirectory(string gameVersion) => Path.Combine(SharedVersionsDirectory, gameVersion);
        public string GetSharedVersionJsonPath(string gameVersion) => Path.Combine(GetSharedVersionDirectory(gameVersion), $"{gameVersion}.json");
        public string GetSharedClientJarPath(string gameVersion) => Path.Combine(GetSharedVersionDirectory(gameVersion), $"{gameVersion}.jar");
        public string GetSharedNativesDirectory(string runtimeKey) => Path.Combine(SharedNativesDirectory, runtimeKey);
        public string GetSharedLoaderDirectory(LoaderType loaderType, string gameVersion, string loaderVersion) => Path.Combine(SharedLoadersDirectory, loaderType.ToString(), gameVersion, loaderVersion);
        public string GetManagedJavaDirectory(string runtimeKey) => Path.Combine(ManagedJavaDirectory, runtimeKey);
        public string GetDefaultInstanceDirectory(string instanceName) => Path.Combine(InstancesDirectory, instanceName);
        public string GetInstanceDataDirectory(string installLocation) => Path.Combine(installLocation, ".blockium");
        public string GetInstanceMetadataFilePath(string installLocation) => Path.Combine(GetInstanceDataDirectory(installLocation), "instance-metadata.json");
        public string GetContextLogFilePath(string context, DateTimeOffset? timestampUtc = null) => Path.Combine(LogsDirectory, $"{context}_{(timestampUtc ?? DateTimeOffset.UtcNow):yyyyMMdd}.log");
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
