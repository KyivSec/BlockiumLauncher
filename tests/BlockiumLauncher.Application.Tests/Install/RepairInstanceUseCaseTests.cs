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

public sealed class RepairInstanceUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenInstanceDoesNotExist()
    {
        var Repository = new FakeInstanceRepository();
        var VerifyUseCase = new VerifyInstanceFilesUseCase(Repository);
        var RepairUseCase = new RepairInstanceUseCase(VerifyUseCase);

        var Result = await RepairUseCase.ExecuteAsync(new RepairInstanceRequest
        {
            InstanceId = InstanceId.New()
        });

        Assert.True(Result.IsFailure);
        Assert.Equal(InstallErrors.InstanceNotFound.Code, Result.Error.Code);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesRootAndManagedDirectories_WhenRootIsMissing()
    {
        var RootPath = Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests", Guid.NewGuid().ToString("N"));
        var Instance = CreateInstance(RootPath);
        var Repository = new FakeInstanceRepository(Instance);
        var VerifyUseCase = new VerifyInstanceFilesUseCase(Repository);
        var RepairUseCase = new RepairInstanceUseCase(VerifyUseCase);

        try
        {
            var Result = await RepairUseCase.ExecuteAsync(new RepairInstanceRequest
            {
                InstanceId = Instance.InstanceId
            });

            Assert.True(Result.IsSuccess);
            Assert.True(Result.Value.Changed);
            Assert.True(Result.Value.Verification.IsValid);
            Assert.True(Directory.Exists(RootPath));
            Assert.True(Directory.Exists(Path.Combine(RootPath, ".minecraft")));
            Assert.True(Directory.Exists(Path.Combine(RootPath, ".blockium")));
        }
        finally
        {
            DeleteDirectoryIfExists(RootPath);
        }
    }

    [Fact]
    public async Task ExecuteAsync_CreatesMissingMinecraftDirectory()
    {
        var RootPath = CreateDirectory();
        Directory.CreateDirectory(Path.Combine(RootPath, ".blockium"));

        try
        {
            var Instance = CreateInstance(RootPath);
            var Repository = new FakeInstanceRepository(Instance);
            var VerifyUseCase = new VerifyInstanceFilesUseCase(Repository);
            var RepairUseCase = new RepairInstanceUseCase(VerifyUseCase);

            var Result = await RepairUseCase.ExecuteAsync(new RepairInstanceRequest
            {
                InstanceId = Instance.InstanceId
            });

            Assert.True(Result.IsSuccess);
            Assert.True(Result.Value.Changed);
            Assert.True(Result.Value.Verification.IsValid);
            Assert.True(Directory.Exists(Path.Combine(RootPath, ".minecraft")));
        }
        finally
        {
            DeleteDirectoryIfExists(RootPath);
        }
    }

    [Fact]
    public async Task ExecuteAsync_CreatesMissingBlockiumDirectory()
    {
        var RootPath = CreateDirectory();
        Directory.CreateDirectory(Path.Combine(RootPath, ".minecraft"));

        try
        {
            var Instance = CreateInstance(RootPath);
            var Repository = new FakeInstanceRepository(Instance);
            var VerifyUseCase = new VerifyInstanceFilesUseCase(Repository);
            var RepairUseCase = new RepairInstanceUseCase(VerifyUseCase);

            var Result = await RepairUseCase.ExecuteAsync(new RepairInstanceRequest
            {
                InstanceId = Instance.InstanceId
            });

            Assert.True(Result.IsSuccess);
            Assert.True(Result.Value.Changed);
            Assert.True(Result.Value.Verification.IsValid);
            Assert.True(Directory.Exists(Path.Combine(RootPath, ".blockium")));
        }
        finally
        {
            DeleteDirectoryIfExists(RootPath);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsUnchanged_WhenInstanceStructureIsAlreadyValid()
    {
        var RootPath = CreateDirectory();
        Directory.CreateDirectory(Path.Combine(RootPath, ".minecraft"));
        Directory.CreateDirectory(Path.Combine(RootPath, ".blockium"));

        try
        {
            var Instance = CreateInstance(RootPath);
            var Repository = new FakeInstanceRepository(Instance);
            var VerifyUseCase = new VerifyInstanceFilesUseCase(Repository);
            var RepairUseCase = new RepairInstanceUseCase(VerifyUseCase);

            var Result = await RepairUseCase.ExecuteAsync(new RepairInstanceRequest
            {
                InstanceId = Instance.InstanceId
            });

            Assert.True(Result.IsSuccess);
            Assert.False(Result.Value.Changed);
            Assert.True(Result.Value.Verification.IsValid);
            Assert.Empty(Result.Value.RepairedPaths);
        }
        finally
        {
            DeleteDirectoryIfExists(RootPath);
        }
    }

    private static LauncherInstance CreateInstance(string InstallLocation)
    {
        return LauncherInstance.Create(
            InstanceId.New(),
            "RepairMe",
            CreateVersionId("1.20.1"),
            LoaderType.Vanilla,
            null,
            InstallLocation,
            DateTimeOffset.UtcNow,
            LaunchProfile.CreateDefault(),
            null);
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
        var Type = typeof(VersionId);

        var ParseMethod = Type.GetMethod("Parse", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(string) }, null);
        if (ParseMethod is not null)
        {
            return (VersionId)ParseMethod.Invoke(null, new object[] { Value })!;
        }

        var CreateMethod = Type.GetMethod("Create", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(string) }, null);
        if (CreateMethod is not null)
        {
            return (VersionId)CreateMethod.Invoke(null, new object[] { Value })!;
        }

        var Constructor = Type.GetConstructor(new[] { typeof(string) });
        if (Constructor is not null)
        {
            return (VersionId)Constructor.Invoke(new object[] { Value });
        }

        throw new InvalidOperationException("Could not create VersionId.");
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