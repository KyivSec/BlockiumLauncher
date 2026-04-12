using BlockiumLauncher.Application.Abstractions.Launch;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Accounts;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Application.UseCases.Launch;
using BlockiumLauncher.Contracts.Launch;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;
using Xunit;

namespace BlockiumLauncher.Application.Tests.Launch;

public sealed class LaunchInstanceUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenPlanBuildFails()
    {
        var AccountRepository = new FakeAccountRepository();
        var ResolveAccountUseCase = new ResolveOfflineLaunchAccountUseCase(AccountRepository);
        var InstanceRepository = new FakeInstanceRepository();
        var BuildLaunchPlanUseCase = new BuildLaunchPlanUseCase(
            InstanceRepository,
            ResolveAccountUseCase,
            new FakeVersionManifestService(),
            new FakeLoaderMetadataService());

        var Runner = new FakeLaunchProcessRunner(Result<LaunchInstanceResult>.Failure(LaunchErrors.ProcessStartFailed));
        var UseCase = new LaunchInstanceUseCase(BuildLaunchPlanUseCase, Runner);

        var Result = await UseCase.ExecuteAsync(new LaunchInstanceRequest
        {
            InstanceId = new InstanceId("missing"),
            JavaExecutablePath = GetDotnetExecutablePath(),
            MainClass = "--info",
            ClasspathEntries = [CreateClasspathFile()]
        });

        Assert.True(Result.IsFailure);
        Assert.Equal(LaunchErrors.InstanceNotFound.Code, Result.Error.Code);
        Assert.False(Runner.WasStartCalled);
    }

    [Fact]
    public async Task ExecuteAsync_StartsProcess_WhenPlanBuildSucceeds()
    {
        var WorkingDirectory = CreateDirectory();
        var ClasspathFile = CreateClasspathFile();

        try
        {
            var Instance = CreateInstalledInstance(WorkingDirectory);
            var Account = LauncherAccount.CreateOffline(new AccountId("offline-1"), "Builder", null, true);
            var AccountRepository = new FakeAccountRepository(Account);
            var ResolveAccountUseCase = new ResolveOfflineLaunchAccountUseCase(AccountRepository);
            var InstanceRepository = new FakeInstanceRepository(Instance);
            var BuildLaunchPlanUseCase = new BuildLaunchPlanUseCase(
                InstanceRepository,
                ResolveAccountUseCase,
                new FakeVersionManifestService(),
                new FakeLoaderMetadataService());

            var DotnetPath = GetDotnetExecutablePath();

            var RunnerResult = Result<LaunchInstanceResult>.Success(new LaunchInstanceResult
            {
                LaunchId = Guid.NewGuid(),
                InstanceId = Instance.InstanceId.ToString(),
                ProcessId = 1234,
                IsRunning = true,
                HasExited = false,
                ExitCode = null,
                OutputLines = [],
                Plan = new LaunchPlanDto
                {
                    InstanceId = Instance.InstanceId.ToString(),
                    AccountId = Account.AccountId.ToString(),
                    JavaExecutablePath = DotnetPath,
                    WorkingDirectory = WorkingDirectory,
                    MainClass = "--info",
                    ClasspathEntries = [Path.GetFullPath(ClasspathFile)],
                    JvmArguments = [],
                    GameArguments = [],
                    EnvironmentVariables = [],
                    IsDryRun = false
                }
            });

            var Runner = new FakeLaunchProcessRunner(RunnerResult);
            var UseCase = new LaunchInstanceUseCase(BuildLaunchPlanUseCase, Runner);

            var Result = await UseCase.ExecuteAsync(new LaunchInstanceRequest
            {
                InstanceId = Instance.InstanceId,
                JavaExecutablePath = DotnetPath,
                MainClass = "--info",
                ClasspathEntries = [ClasspathFile]
            });

            Assert.True(Result.IsSuccess);
            Assert.True(Result.Value.IsRunning);
            Assert.True(Runner.WasStartCalled);
            Assert.NotNull(Runner.LastPlan);
            Assert.False(Runner.LastPlan!.IsDryRun);
            Assert.Equal(Instance.InstanceId.ToString(), Runner.LastPlan.InstanceId);
            Assert.Equal(Account.AccountId.ToString(), Runner.LastPlan.AccountId);
            Assert.Equal(Path.GetFullPath(DotnetPath), Runner.LastPlan.JavaExecutablePath);
            Assert.Equal("--info", Runner.LastPlan.MainClass);
        }
        finally
        {
            DeleteDirectoryIfExists(WorkingDirectory);
            DeleteFileContainer(ClasspathFile);
        }
    }

    private static string GetDotnetExecutablePath()
    {
        var DotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(DotnetRoot))
        {
            var Candidate = Path.Combine(DotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(Candidate))
            {
                return Candidate;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            var Candidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
            if (File.Exists(Candidate))
            {
                return Candidate;
            }
        }
        else
        {
            foreach (var Candidate in new[] { "/usr/bin/dotnet", "/usr/local/bin/dotnet" })
            {
                if (File.Exists(Candidate))
                {
                    return Candidate;
                }
            }
        }

        throw new FileNotFoundException("Could not resolve the dotnet executable path.");
    }

    private static LauncherInstance CreateInstalledInstance(string InstallLocation)
    {
        var Instance = LauncherInstance.Create(
            new InstanceId("instance-1"),
            "LaunchMe",
            new VersionId("1.20.1"),
            LoaderType.Vanilla,
            null,
            InstallLocation,
            DateTimeOffset.UtcNow,
            LaunchProfile.CreateDefault(),
            null);

        Instance.MarkInstalled();
        return Instance;
    }

    private static string CreateDirectory()
    {
        var PathValue = Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(PathValue);
        return PathValue;
    }

    private static string CreateClasspathFile()
    {
        var DirectoryPath = CreateDirectory();
        var FilePath = Path.Combine(DirectoryPath, "client.jar");
        File.WriteAllText(FilePath, string.Empty);
        return FilePath;
    }

    private static void DeleteDirectoryIfExists(string PathValue)
    {
        if (Directory.Exists(PathValue))
        {
            Directory.Delete(PathValue, true);
        }
    }

    private static void DeleteFileContainer(string PathValue)
    {
        if (File.Exists(PathValue))
        {
            File.Delete(PathValue);
        }

        var DirectoryPath = Path.GetDirectoryName(PathValue);
        if (!string.IsNullOrWhiteSpace(DirectoryPath) && Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, true);
        }
    }

    private sealed class FakeLaunchProcessRunner : ILaunchProcessRunner
    {
        private readonly Result<LaunchInstanceResult> StartResult;

        public bool WasStartCalled { get; private set; }
        public LaunchPlanDto? LastPlan { get; private set; }

        public FakeLaunchProcessRunner(Result<LaunchInstanceResult> StartResult)
        {
            this.StartResult = StartResult;
        }

        public Task<Result<LaunchInstanceResult>> StartAsync(LaunchPlanDto Plan, CancellationToken CancellationToken = default)
        {
            WasStartCalled = true;
            LastPlan = Plan;
            return Task.FromResult(StartResult);
        }

        public Task<Result<LaunchInstanceResult>> GetStatusAsync(Guid LaunchId, CancellationToken CancellationToken = default)
        {
            return Task.FromResult(Result<LaunchInstanceResult>.Failure(LaunchErrors.LaunchSessionNotFound));
        }

        public Task<Result> StopAsync(Guid LaunchId, CancellationToken CancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }

        public Task<Result<IReadOnlyList<LaunchOutputLine>>> GetLatestOutputAsync(string instanceId, CancellationToken CancellationToken = default)
        {
            return Task.FromResult(Result<IReadOnlyList<LaunchOutputLine>>.Success(Array.Empty<LaunchOutputLine>()));
        }

        public Task<Result> ClearLatestOutputAsync(string instanceId, CancellationToken CancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class FakeInstanceRepository : IInstanceRepository
    {
        private readonly List<LauncherInstance> Instances = [];

        public FakeInstanceRepository(params LauncherInstance[] Instances)
        {
            this.Instances.AddRange(Instances);
        }

        public Task<IReadOnlyList<LauncherInstance>> ListAsync(CancellationToken CancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LauncherInstance>>(Instances.ToList());
        }

        public Task<LauncherInstance?> GetByIdAsync(InstanceId InstanceId, CancellationToken CancellationToken)
        {
            return Task.FromResult(Instances.FirstOrDefault(x => x.InstanceId == InstanceId));
        }

        public Task<LauncherInstance?> GetByNameAsync(string Name, CancellationToken CancellationToken)
        {
            return Task.FromResult(Instances.FirstOrDefault(x => string.Equals(x.Name, Name, StringComparison.Ordinal)));
        }

        public Task SaveAsync(LauncherInstance Instance, CancellationToken CancellationToken)
        {
            var ExistingIndex = Instances.FindIndex(x => x.InstanceId == Instance.InstanceId);
            if (ExistingIndex >= 0)
            {
                Instances[ExistingIndex] = Instance;
            }
            else
            {
                Instances.Add(Instance);
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(InstanceId InstanceId, CancellationToken CancellationToken)
        {
            Instances.RemoveAll(x => x.InstanceId == InstanceId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAccountRepository : IAccountRepository
    {
        private readonly List<LauncherAccount> Accounts = [];

        public FakeAccountRepository(params LauncherAccount[] Accounts)
        {
            this.Accounts.AddRange(Accounts);
        }

        public Task<IReadOnlyList<LauncherAccount>> ListAsync(CancellationToken CancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LauncherAccount>>(Accounts.ToList());
        }

        public Task<LauncherAccount?> GetByIdAsync(AccountId AccountId, CancellationToken CancellationToken = default)
        {
            return Task.FromResult(Accounts.FirstOrDefault(x => x.AccountId == AccountId));
        }

        public Task<LauncherAccount?> GetDefaultAsync(CancellationToken CancellationToken = default)
        {
            return Task.FromResult(Accounts.FirstOrDefault(x => x.IsDefault));
        }

        public Task SaveAsync(LauncherAccount Account, CancellationToken CancellationToken = default)
        {
            var ExistingIndex = Accounts.FindIndex(x => x.AccountId == Account.AccountId);
            if (ExistingIndex >= 0)
            {
                Accounts[ExistingIndex] = Account;
            }
            else
            {
                Accounts.Add(Account);
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(AccountId AccountId, CancellationToken CancellationToken = default)
        {
            Accounts.RemoveAll(x => x.AccountId == AccountId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeVersionManifestService : IVersionManifestService
    {
        public Task<Result<IReadOnlyList<VersionSummary>>> GetAvailableVersionsAsync(CancellationToken CancellationToken)
        {
            IReadOnlyList<VersionSummary> Versions = Array.Empty<VersionSummary>();
            return Task.FromResult(Result<IReadOnlyList<VersionSummary>>.Success(Versions));
        }

        public Task<Result<VersionSummary?>> GetVersionAsync(VersionId VersionId, CancellationToken CancellationToken)
        {
            return Task.FromResult(Result<VersionSummary?>.Success(new VersionSummary(
                new VersionId("1.20.1"),
                "1.20.1",
                true,
                DateTimeOffset.UtcNow)));
        }
    }

    private sealed class FakeLoaderMetadataService : ILoaderMetadataService
    {
        public Task<Result<IReadOnlyList<LoaderVersionSummary>>> GetLoaderVersionsAsync(
            LoaderType LoaderType,
            VersionId GameVersion,
            CancellationToken CancellationToken)
        {
            return Task.FromResult(Result<IReadOnlyList<LoaderVersionSummary>>.Success(
            [
                new LoaderVersionSummary(LoaderType.Fabric, new VersionId("1.20.1"), "0.15.0", true)
            ]));
        }
    }
}
