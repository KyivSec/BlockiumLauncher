using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using Xunit;

namespace BlockiumLauncher.Application.Tests.Install;

public sealed class BuildUpdatePlanUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenInstanceDoesNotExist()
    {
        var Repository = new FakeInstanceRepository();
        var VerifyUseCase = new VerifyInstanceFilesUseCase(Repository);
        var UseCase = new BuildUpdatePlanUseCase(Repository, VerifyUseCase);

        var Result = await UseCase.ExecuteAsync(new UpdateInstanceRequest
        {
            InstanceId = InstanceId.New(),
            TargetGameVersion = CreateVersionId("1.20.1"),
            TargetLoaderType = LoaderType.Vanilla,
            TargetLoaderVersion = null
        });

        Assert.True(Result.IsFailure);
        Assert.Equal(InstallErrors.InstanceNotFound.Code, Result.Error.Code);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNoOpPlan_WhenRequestedStateMatchesCurrentInstance()
    {
        var RootPath = CreateValidInstanceRoot();

        try
        {
            var Instance = CreateInstance(RootPath, "1.20.1", LoaderType.Fabric, "0.15.0");
            var Repository = new FakeInstanceRepository(Instance);
            var VerifyUseCase = new VerifyInstanceFilesUseCase(Repository);
            var UseCase = new BuildUpdatePlanUseCase(Repository, VerifyUseCase);

            var Result = await UseCase.ExecuteAsync(new UpdateInstanceRequest
            {
                InstanceId = Instance.InstanceId,
                TargetGameVersion = CreateVersionId("1.20.1"),
                TargetLoaderType = LoaderType.Fabric,
                TargetLoaderVersion = CreateVersionId("0.15.0")
            });

            Assert.True(Result.IsSuccess);
            Assert.True(Result.Value.IsNoOp);
            Assert.False(Result.Value.RequiresRepair);
            Assert.Contains(Result.Value.Steps, x => x.Kind == UpdatePlanStepKind.NoOp);
        }
        finally
        {
            DeleteDirectoryIfExists(RootPath);
        }
    }

    [Fact]
    public async Task ExecuteAsync_PlansManagedContentUpdate_WhenGameVersionChanges()
    {
        var RootPath = CreateValidInstanceRoot();

        try
        {
            var Instance = CreateInstance(RootPath, "1.20.1", LoaderType.Vanilla, null);
            var Repository = new FakeInstanceRepository(Instance);
            var VerifyUseCase = new VerifyInstanceFilesUseCase(Repository);
            var UseCase = new BuildUpdatePlanUseCase(Repository, VerifyUseCase);

            var Result = await UseCase.ExecuteAsync(new UpdateInstanceRequest
            {
                InstanceId = Instance.InstanceId,
                TargetGameVersion = CreateVersionId("1.21.1"),
                TargetLoaderType = LoaderType.Vanilla,
                TargetLoaderVersion = null
            });

            Assert.True(Result.IsSuccess);
            Assert.False(Result.Value.IsNoOp);
            Assert.Contains(Result.Value.Steps, x => x.Kind == UpdatePlanStepKind.UpdateManagedContent);
            Assert.Contains(Result.Value.Steps, x => x.Kind == UpdatePlanStepKind.PersistMetadata);
        }
        finally
        {
            DeleteDirectoryIfExists(RootPath);
        }
    }

    [Fact]
    public async Task ExecuteAsync_PlansManagedContentUpdate_WhenLoaderChanges()
    {
        var RootPath = CreateValidInstanceRoot();

        try
        {
            var Instance = CreateInstance(RootPath, "1.20.1", LoaderType.Vanilla, null);
            var Repository = new FakeInstanceRepository(Instance);
            var VerifyUseCase = new VerifyInstanceFilesUseCase(Repository);
            var UseCase = new BuildUpdatePlanUseCase(Repository, VerifyUseCase);

            var Result = await UseCase.ExecuteAsync(new UpdateInstanceRequest
            {
                InstanceId = Instance.InstanceId,
                TargetGameVersion = CreateVersionId("1.20.1"),
                TargetLoaderType = LoaderType.Forge,
                TargetLoaderVersion = CreateVersionId("47.3.0")
            });

            Assert.True(Result.IsSuccess);
            Assert.False(Result.Value.IsNoOp);
            Assert.Contains(Result.Value.Steps, x => x.Kind == UpdatePlanStepKind.UpdateManagedContent);
            Assert.Contains(Result.Value.Steps, x => x.Kind == UpdatePlanStepKind.PersistMetadata);
        }
        finally
        {
            DeleteDirectoryIfExists(RootPath);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RequiresRepair_WhenInstanceStructureIsInvalid()
    {
        var RootPath = CreateDirectory();
        Directory.CreateDirectory(Path.Combine(RootPath, ".minecraft"));

        try
        {
            var Instance = CreateInstance(RootPath, "1.20.1", LoaderType.Vanilla, null);
            var Repository = new FakeInstanceRepository(Instance);
            var VerifyUseCase = new VerifyInstanceFilesUseCase(Repository);
            var UseCase = new BuildUpdatePlanUseCase(Repository, VerifyUseCase);

            var Result = await UseCase.ExecuteAsync(new UpdateInstanceRequest
            {
                InstanceId = Instance.InstanceId,
                TargetGameVersion = CreateVersionId("1.21.1"),
                TargetLoaderType = LoaderType.Vanilla,
                TargetLoaderVersion = null
            });

            Assert.True(Result.IsSuccess);
            Assert.True(Result.Value.RequiresRepair);
            Assert.Contains(Result.Value.Steps, x => x.Kind == UpdatePlanStepKind.RepairStructure);
        }
        finally
        {
            DeleteDirectoryIfExists(RootPath);
        }
    }

    private static LauncherInstance CreateInstance(string InstallLocation, string GameVersion, LoaderType LoaderType, string? LoaderVersion)
    {
        return LauncherInstance.Create(
            InstanceId.New(),
            "UpdateMe",
            CreateVersionId(GameVersion),
            LoaderType,
            LoaderVersion is null ? null : CreateVersionId(LoaderVersion),
            InstallLocation,
            DateTimeOffset.UtcNow,
            LaunchProfile.CreateDefault(),
            null);
    }

    private static string CreateValidInstanceRoot()
    {
        var RootPath = CreateDirectory();
        Directory.CreateDirectory(Path.Combine(RootPath, ".minecraft"));
        Directory.CreateDirectory(Path.Combine(RootPath, ".blockium"));
        return RootPath;
    }

    private static string CreateDirectory()
    {
        var PathValue = Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(PathValue);
        return PathValue;
    }

    private static void DeleteDirectoryIfExists(string PathValue)
    {
        if (Directory.Exists(PathValue))
        {
            Directory.Delete(PathValue, true);
        }
    }

    private static VersionId CreateVersionId(string Value)
    {
        return new VersionId(Value);
    }

    private sealed class FakeInstanceRepository : IInstanceRepository
    {
        private readonly LauncherInstance? Instance;

        public FakeInstanceRepository(LauncherInstance? Instance = null)
        {
            this.Instance = Instance;
        }

        public Task<IReadOnlyList<LauncherInstance>> ListAsync(CancellationToken CancellationToken = default)
            => Task.FromResult<IReadOnlyList<LauncherInstance>>([]);

        public Task<LauncherInstance?> GetByIdAsync(InstanceId InstanceId, CancellationToken CancellationToken = default)
        {
            if (Instance is not null && Instance.InstanceId == InstanceId)
            {
                return Task.FromResult<LauncherInstance?>(Instance);
            }

            return Task.FromResult<LauncherInstance?>(null);
        }

        public Task<LauncherInstance?> GetByNameAsync(string Name, CancellationToken CancellationToken = default)
            => Task.FromResult<LauncherInstance?>(null);

        public Task SaveAsync(LauncherInstance Instance, CancellationToken CancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(InstanceId InstanceId, CancellationToken CancellationToken = default)
            => Task.CompletedTask;
    }
}