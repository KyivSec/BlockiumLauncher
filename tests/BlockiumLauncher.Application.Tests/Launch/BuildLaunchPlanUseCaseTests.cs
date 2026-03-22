using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Accounts;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Application.UseCases.Launch;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;
using Xunit;

namespace BlockiumLauncher.Application.Tests.Launch;

public sealed class BuildLaunchPlanUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenInstanceDoesNotExist()
    {
        var AccountRepository = new FakeAccountRepository(
            LauncherAccount.CreateOffline(new AccountId("offline-1"), "Builder", null, true));
        var InstanceRepository = new FakeInstanceRepository();
        var ResolveAccountUseCase = new ResolveOfflineLaunchAccountUseCase(AccountRepository);
        var UseCase = CreateUseCase(InstanceRepository, ResolveAccountUseCase);

        var JavaExecutablePath = CreateJavaFile();
        var ClasspathFile = CreateClasspathFile();

        try
        {
            var Result = await UseCase.ExecuteAsync(new BuildLaunchPlanRequest
            {
                InstanceId = new InstanceId("missing"),
                JavaExecutablePath = JavaExecutablePath,
                MainClass = "net.minecraft.client.main.Main",
                ClasspathEntries = [ClasspathFile],
                IsDryRun = true
            });

            Assert.True(Result.IsFailure);
            Assert.Equal(LaunchErrors.InstanceNotFound.Code, Result.Error.Code);
        }
        finally
        {
            DeleteFileContainer(JavaExecutablePath);
            DeleteFileContainer(ClasspathFile);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenOfflineAccountIsMissing()
    {
        var WorkingDirectory = CreateDirectory();
        var JavaExecutablePath = CreateJavaFile();
        var ClasspathFile = CreateClasspathFile();

        try
        {
            var Instance = CreateInstalledInstance(WorkingDirectory, LoaderType.Vanilla, null);
            var AccountRepository = new FakeAccountRepository();
            var InstanceRepository = new FakeInstanceRepository(Instance);
            var ResolveAccountUseCase = new ResolveOfflineLaunchAccountUseCase(AccountRepository);
            var UseCase = CreateUseCase(InstanceRepository, ResolveAccountUseCase);

            var Result = await UseCase.ExecuteAsync(new BuildLaunchPlanRequest
            {
                InstanceId = Instance.InstanceId,
                JavaExecutablePath = JavaExecutablePath,
                MainClass = "net.minecraft.client.main.Main",
                ClasspathEntries = [ClasspathFile],
                IsDryRun = true
            });

            Assert.True(Result.IsFailure);
            Assert.Equal(AccountErrors.NoOfflineAccount.Code, Result.Error.Code);
        }
        finally
        {
            DeleteDirectoryIfExists(WorkingDirectory);
            DeleteFileContainer(JavaExecutablePath);
            DeleteFileContainer(ClasspathFile);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenJavaExecutableIsMissing()
    {
        var WorkingDirectory = CreateDirectory();
        var ClasspathFile = CreateClasspathFile();

        try
        {
            var Instance = CreateInstalledInstance(WorkingDirectory, LoaderType.Vanilla, null);
            var Account = LauncherAccount.CreateOffline(new AccountId("offline-1"), "Builder", null, true);
            var AccountRepository = new FakeAccountRepository(Account);
            var InstanceRepository = new FakeInstanceRepository(Instance);
            var ResolveAccountUseCase = new ResolveOfflineLaunchAccountUseCase(AccountRepository);
            var UseCase = CreateUseCase(InstanceRepository, ResolveAccountUseCase);

            var Result = await UseCase.ExecuteAsync(new BuildLaunchPlanRequest
            {
                InstanceId = Instance.InstanceId,
                JavaExecutablePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "java.exe"),
                MainClass = "net.minecraft.client.main.Main",
                ClasspathEntries = [ClasspathFile],
                IsDryRun = true
            });

            Assert.True(Result.IsFailure);
            Assert.Equal(LaunchErrors.JavaExecutableMissing.Code, Result.Error.Code);
        }
        finally
        {
            DeleteDirectoryIfExists(WorkingDirectory);
            DeleteFileContainer(ClasspathFile);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenVersionMetadataIsMissing()
    {
        var WorkingDirectory = CreateDirectory();
        var JavaExecutablePath = CreateJavaFile();
        var ClasspathFile = CreateClasspathFile();

        try
        {
            var Instance = CreateInstalledInstance(WorkingDirectory, LoaderType.Vanilla, null);
            var Account = LauncherAccount.CreateOffline(new AccountId("offline-1"), "Builder", null, true);
            var AccountRepository = new FakeAccountRepository(Account);
            var InstanceRepository = new FakeInstanceRepository(Instance);
            var ResolveAccountUseCase = new ResolveOfflineLaunchAccountUseCase(AccountRepository);
            var UseCase = CreateUseCase(
                InstanceRepository,
                ResolveAccountUseCase,
                new FakeVersionManifestService(Result<VersionSummary?>.Success(null)),
                new FakeLoaderMetadataService());

            var Result = await UseCase.ExecuteAsync(new BuildLaunchPlanRequest
            {
                InstanceId = Instance.InstanceId,
                JavaExecutablePath = JavaExecutablePath,
                MainClass = "net.minecraft.client.main.Main",
                ClasspathEntries = [ClasspathFile],
                IsDryRun = true
            });

            Assert.True(Result.IsFailure);
            Assert.Equal(LaunchErrors.VersionMetadataMissing.Code, Result.Error.Code);
        }
        finally
        {
            DeleteDirectoryIfExists(WorkingDirectory);
            DeleteFileContainer(JavaExecutablePath);
            DeleteFileContainer(ClasspathFile);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenLoaderMetadataIsMissing()
    {
        var WorkingDirectory = CreateDirectory();
        var JavaExecutablePath = CreateJavaFile();
        var ClasspathFile = CreateClasspathFile();

        try
        {
            var Instance = CreateInstalledInstance(WorkingDirectory, LoaderType.Fabric, new VersionId("0.15.0"));
            var Account = LauncherAccount.CreateOffline(new AccountId("offline-1"), "Builder", null, true);
            var AccountRepository = new FakeAccountRepository(Account);
            var InstanceRepository = new FakeInstanceRepository(Instance);
            var ResolveAccountUseCase = new ResolveOfflineLaunchAccountUseCase(AccountRepository);
            var UseCase = CreateUseCase(
                InstanceRepository,
                ResolveAccountUseCase,
                new FakeVersionManifestService(),
                new FakeLoaderMetadataService(Result<IReadOnlyList<LoaderVersionSummary>>.Success([])));

            var Result = await UseCase.ExecuteAsync(new BuildLaunchPlanRequest
            {
                InstanceId = Instance.InstanceId,
                JavaExecutablePath = JavaExecutablePath,
                MainClass = "net.minecraft.client.main.Main",
                ClasspathEntries = [ClasspathFile],
                IsDryRun = true
            });

            Assert.True(Result.IsFailure);
            Assert.Equal(LaunchErrors.LoaderMetadataMissing.Code, Result.Error.Code);
        }
        finally
        {
            DeleteDirectoryIfExists(WorkingDirectory);
            DeleteFileContainer(JavaExecutablePath);
            DeleteFileContainer(ClasspathFile);
        }
    }

    [Fact]
    public async Task ExecuteAsync_BuildsFullDryRunPlanSuccessfully()
    {
        var WorkingDirectory = CreateDirectory();
        var JavaExecutablePath = CreateJavaFile();
        var ClasspathFile = CreateClasspathFile();
        var AssetsDirectory = CreateDirectory();

        try
        {
            var Instance = CreateInstalledInstance(WorkingDirectory, LoaderType.Fabric, new VersionId("0.15.0"));
            Instance.ChangeLaunchProfile(new LaunchProfile(
                MinMemoryMb: 1024,
                MaxMemoryMb: 2048,
                ExtraJvmArgs: ["-Ddemo=true"],
                ExtraGameArgs: ["--width", "1280"],
                EnvironmentVariables: [new KeyValuePair<string, string>("BLOCKIUM_ENV", "1")]));

            var Account = LauncherAccount.CreateOffline(new AccountId("offline-1"), "Builder", null, true);
            var AccountRepository = new FakeAccountRepository(Account);
            var InstanceRepository = new FakeInstanceRepository(Instance);
            var ResolveAccountUseCase = new ResolveOfflineLaunchAccountUseCase(AccountRepository);
            var UseCase = CreateUseCase(InstanceRepository, ResolveAccountUseCase);

            var Result = await UseCase.ExecuteAsync(new BuildLaunchPlanRequest
            {
                InstanceId = Instance.InstanceId,
                JavaExecutablePath = JavaExecutablePath,
                MainClass = "net.fabricmc.loader.impl.launch.knot.KnotClient",
                AssetsDirectory = AssetsDirectory,
                AssetIndexId = "17",
                ClasspathEntries = [ClasspathFile],
                IsDryRun = true
            });

            Assert.True(Result.IsSuccess);
            Assert.True(Result.Value.IsDryRun);
            Assert.Equal(Instance.InstanceId.ToString(), Result.Value.InstanceId);
            Assert.Equal(Account.AccountId.ToString(), Result.Value.AccountId);
            Assert.Equal(Path.GetFullPath(JavaExecutablePath), Result.Value.JavaExecutablePath);
            Assert.Equal(Path.GetFullPath(WorkingDirectory), Result.Value.WorkingDirectory);
            Assert.Equal("net.fabricmc.loader.impl.launch.knot.KnotClient", Result.Value.MainClass);
            Assert.Equal(Path.GetFullPath(AssetsDirectory), Result.Value.AssetsDirectory);
            Assert.Equal("17", Result.Value.AssetIndexId);
            Assert.Single(Result.Value.ClasspathEntries);
            Assert.Equal(Path.GetFullPath(ClasspathFile), Result.Value.ClasspathEntries[0]);

            Assert.Equal(
                ["-Xms1024m", "-Xmx2048m", "-cp", Path.GetFullPath(ClasspathFile), "-Ddemo=true"],
                Result.Value.JvmArguments.Select(x => x.Value).ToArray());

            Assert.Contains(Result.Value.GameArguments, x => x.Value == "--username");
            Assert.Contains(Result.Value.GameArguments, x => x.Value == "Builder");
            Assert.Contains(Result.Value.GameArguments, x => x.Value == "--uuid");
            Assert.Contains(Result.Value.GameArguments, x => x.Value == ResolveOfflineLaunchAccountUseCase.CreateOfflinePlayerUuid("Builder"));
            Assert.Contains(Result.Value.GameArguments, x => x.Value == "--gameDir");
            Assert.Contains(Result.Value.GameArguments, x => x.Value == Path.GetFullPath(WorkingDirectory));
            Assert.Contains(Result.Value.GameArguments, x => x.Value == "--version");
            Assert.Contains(Result.Value.GameArguments, x => x.Value == "1.20.1");
            Assert.Contains(Result.Value.GameArguments, x => x.Value == "--assetsDir");
            Assert.Contains(Result.Value.GameArguments, x => x.Value == Path.GetFullPath(AssetsDirectory));
            Assert.Contains(Result.Value.GameArguments, x => x.Value == "--assetIndex");
            Assert.Contains(Result.Value.GameArguments, x => x.Value == "17");
            Assert.Contains(Result.Value.GameArguments, x => x.Value == "--loader");
            Assert.Contains(Result.Value.GameArguments, x => x.Value == "Fabric");
            Assert.Contains(Result.Value.GameArguments, x => x.Value == "--loaderVersion");
            Assert.Contains(Result.Value.GameArguments, x => x.Value == "0.15.0");
            Assert.Contains(Result.Value.GameArguments, x => x.Value == "--width");
            Assert.Contains(Result.Value.GameArguments, x => x.Value == "1280");

            Assert.Single(Result.Value.EnvironmentVariables);
            Assert.Equal("BLOCKIUM_ENV", Result.Value.EnvironmentVariables[0].Name);
            Assert.Equal("1", Result.Value.EnvironmentVariables[0].Value);
        }
        finally
        {
            DeleteDirectoryIfExists(WorkingDirectory);
            DeleteFileContainer(JavaExecutablePath);
            DeleteFileContainer(ClasspathFile);
            DeleteDirectoryIfExists(AssetsDirectory);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ProducesDeterministicArguments_ForSameInputs()
    {
        var WorkingDirectory = CreateDirectory();
        var JavaExecutablePath = CreateJavaFile();
        var ClasspathFile = CreateClasspathFile();
        var AssetsDirectory = CreateDirectory();

        try
        {
            var Instance = CreateInstalledInstance(WorkingDirectory, LoaderType.Vanilla, null);
            var Account = LauncherAccount.CreateOffline(new AccountId("offline-1"), "Builder", null, true);
            var AccountRepository = new FakeAccountRepository(Account);
            var InstanceRepository = new FakeInstanceRepository(Instance);
            var ResolveAccountUseCase = new ResolveOfflineLaunchAccountUseCase(AccountRepository);
            var UseCase = CreateUseCase(InstanceRepository, ResolveAccountUseCase);

            var First = await UseCase.ExecuteAsync(new BuildLaunchPlanRequest
            {
                InstanceId = Instance.InstanceId,
                JavaExecutablePath = JavaExecutablePath,
                MainClass = "net.minecraft.client.main.Main",
                AssetsDirectory = AssetsDirectory,
                AssetIndexId = "17",
                ClasspathEntries = [ClasspathFile],
                IsDryRun = true
            });

            var Second = await UseCase.ExecuteAsync(new BuildLaunchPlanRequest
            {
                InstanceId = Instance.InstanceId,
                JavaExecutablePath = JavaExecutablePath,
                MainClass = "net.minecraft.client.main.Main",
                AssetsDirectory = AssetsDirectory,
                AssetIndexId = "17",
                ClasspathEntries = [ClasspathFile],
                IsDryRun = true
            });

            Assert.True(First.IsSuccess);
            Assert.True(Second.IsSuccess);
            Assert.Equal(First.Value.JvmArguments.Select(x => x.Value).ToArray(), Second.Value.JvmArguments.Select(x => x.Value).ToArray());
            Assert.Equal(First.Value.GameArguments.Select(x => x.Value).ToArray(), Second.Value.GameArguments.Select(x => x.Value).ToArray());
        }
        finally
        {
            DeleteDirectoryIfExists(WorkingDirectory);
            DeleteFileContainer(JavaExecutablePath);
            DeleteFileContainer(ClasspathFile);
            DeleteDirectoryIfExists(AssetsDirectory);
        }
    }

    private static BuildLaunchPlanUseCase CreateUseCase(
        IInstanceRepository InstanceRepository,
        ResolveOfflineLaunchAccountUseCase ResolveAccountUseCase,
        IVersionManifestService? VersionManifestService = null,
        ILoaderMetadataService? LoaderMetadataService = null)
    {
        return new BuildLaunchPlanUseCase(
            InstanceRepository,
            ResolveAccountUseCase,
            VersionManifestService ?? new FakeVersionManifestService(),
            LoaderMetadataService ?? new FakeLoaderMetadataService());
    }

    private static LauncherInstance CreateInstalledInstance(string InstallLocation, LoaderType LoaderType, VersionId? LoaderVersion)
    {
        var Instance = LauncherInstance.Create(
            new InstanceId("instance-1"),
            "LaunchMe",
            new VersionId("1.20.1"),
            LoaderType,
            LoaderVersion,
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

    private static string CreateJavaFile()
    {
        var DirectoryPath = CreateDirectory();
        var FileName = OperatingSystem.IsWindows() ? "java.exe" : "java";
        var FilePath = Path.Combine(DirectoryPath, FileName);
        File.WriteAllText(FilePath, string.Empty);
        return FilePath;
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
        private readonly Result<VersionSummary?> VersionResult;

        public FakeVersionManifestService()
        {
            VersionResult = Result<VersionSummary?>.Success(new VersionSummary(
                new VersionId("1.20.1"),
                "1.20.1",
                true,
                DateTimeOffset.UtcNow));
        }

        public FakeVersionManifestService(Result<VersionSummary?> VersionResult)
        {
            this.VersionResult = VersionResult;
        }

        public Task<Result<IReadOnlyList<VersionSummary>>> GetAvailableVersionsAsync(CancellationToken CancellationToken)
        {
            return Task.FromResult(Result<IReadOnlyList<VersionSummary>>.Success([]));
        }

        public Task<Result<VersionSummary?>> GetVersionAsync(VersionId VersionId, CancellationToken CancellationToken)
        {
            return Task.FromResult(VersionResult);
        }
    }

    private sealed class FakeLoaderMetadataService : ILoaderMetadataService
    {
        private readonly Result<IReadOnlyList<LoaderVersionSummary>> LoaderResult;

        public FakeLoaderMetadataService()
        {
            LoaderResult = Result<IReadOnlyList<LoaderVersionSummary>>.Success(
            [
                new LoaderVersionSummary(LoaderType.Fabric, new VersionId("1.20.1"), "0.15.0", true)
            ]);
        }

        public FakeLoaderMetadataService(Result<IReadOnlyList<LoaderVersionSummary>> LoaderResult)
        {
            this.LoaderResult = LoaderResult;
        }

        public Task<Result<IReadOnlyList<LoaderVersionSummary>>> GetLoaderVersionsAsync(
            LoaderType LoaderType,
            VersionId GameVersion,
            CancellationToken CancellationToken)
        {
            return Task.FromResult(LoaderResult);
        }
    }
}