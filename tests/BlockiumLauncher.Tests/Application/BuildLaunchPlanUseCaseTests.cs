using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.Diagnostics;
using BlockiumLauncher.Application.UseCases.Accounts;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Application.UseCases.Launch;
using BlockiumLauncher.Application.UseCases.Skins;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;
using System.Text;
using System.Text.Json;
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

    [Fact]
    public async Task ExecuteAsync_EmitsSkinAndCapeUserProperties_WhenAccountAppearanceExists()
    {
        var workingDirectory = CreateDirectory();
        var javaExecutablePath = CreateJavaFile();
        var classpathFile = CreateClasspathFile();
        var assetsDirectory = CreateDirectory();
        var skinPath = Path.Combine(CreateDirectory(), "selected-skin.png");
        var capePath = Path.Combine(CreateDirectory(), "selected-cape.png");
        File.WriteAllBytes(skinPath, [1, 2, 3, 4]);
        File.WriteAllBytes(capePath, [5, 6, 7, 8]);

        try
        {
            var account = LauncherAccount.CreateOffline(new AccountId("offline-1"), "Builder", null, true);
            var instance = CreateInstalledInstance(workingDirectory, LoaderType.Vanilla, null);
            var accountRepository = new FakeAccountRepository(account);
            var resolveAccountUseCase = new ResolveOfflineLaunchAccountUseCase(accountRepository);
            var skinRepository = new FakeSkinLibraryRepository(
                new SkinAssetSummary
                {
                    SkinId = "skin-1",
                    DisplayName = "Selected Skin",
                    FileName = "selected-skin.png",
                    StoragePath = skinPath,
                    ModelType = SkinModelType.Slim,
                    ImportedAtUtc = DateTimeOffset.UtcNow
                },
                new CapeAssetSummary
                {
                    CapeId = "cape-1",
                    DisplayName = "Selected Cape",
                    FileName = "selected-cape.png",
                    StoragePath = capePath,
                    ImportedAtUtc = DateTimeOffset.UtcNow
                });
            var appearanceRepository = new FakeAccountAppearanceRepository(new AccountAppearanceSelection
            {
                AccountId = account.AccountId,
                SelectedSkinId = "skin-1",
                SelectedCapeId = "cape-1"
            });

            var useCase = CreateUseCase(
                new FakeInstanceRepository(instance),
                resolveAccountUseCase,
                skinLibraryRepository: skinRepository,
                accountAppearanceRepository: appearanceRepository);

            var result = await useCase.ExecuteAsync(new BuildLaunchPlanRequest
            {
                InstanceId = instance.InstanceId,
                JavaExecutablePath = javaExecutablePath,
                MainClass = "net.minecraft.client.main.Main",
                AssetsDirectory = assetsDirectory,
                AssetIndexId = "17",
                ClasspathEntries = [classpathFile],
                IsDryRun = true
            });

            Assert.True(result.IsSuccess);
            var gameArguments = result.Value.GameArguments.Select(argument => argument.Value).ToArray();
            var userPropertiesIndex = Array.IndexOf(gameArguments, "--userProperties");
            Assert.True(userPropertiesIndex >= 0);
            var userPropertiesJson = gameArguments[userPropertiesIndex + 1];
            Assert.NotEqual("{}", userPropertiesJson);

            using var userPropertiesDocument = JsonDocument.Parse(userPropertiesJson);
            var textureProperty = Assert.Single(userPropertiesDocument.RootElement.GetProperty("textures").EnumerateArray());
            var encodedTexturePayload = textureProperty.GetProperty("value").GetString();
            Assert.False(string.IsNullOrWhiteSpace(encodedTexturePayload));

            var decodedTexturePayload = Encoding.UTF8.GetString(Convert.FromBase64String(encodedTexturePayload!));
            using var texturePayloadDocument = JsonDocument.Parse(decodedTexturePayload);
            var texturesElement = texturePayloadDocument.RootElement.GetProperty("textures");
            Assert.StartsWith("file:///", texturesElement.GetProperty("SKIN").GetProperty("url").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("slim", texturesElement.GetProperty("SKIN").GetProperty("metadata").GetProperty("model").GetString());
            Assert.StartsWith("file:///", texturesElement.GetProperty("CAPE").GetProperty("url").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(workingDirectory);
            DeleteFileContainer(javaExecutablePath);
            DeleteFileContainer(classpathFile);
            DeleteDirectoryIfExists(assetsDirectory);
            DeleteFileContainer(skinPath);
            DeleteFileContainer(capePath);
        }
    }

    private static BuildLaunchPlanUseCase CreateUseCase(
        IInstanceRepository InstanceRepository,
        ResolveOfflineLaunchAccountUseCase ResolveAccountUseCase,
        IVersionManifestService? VersionManifestService = null,
        ILoaderMetadataService? LoaderMetadataService = null,
        ISkinLibraryRepository? skinLibraryRepository = null,
        IAccountAppearanceRepository? accountAppearanceRepository = null)
    {
        return new BuildLaunchPlanUseCase(
            InstanceRepository,
            ResolveAccountUseCase,
            VersionManifestService ?? new FakeVersionManifestService(),
            LoaderMetadataService ?? new FakeLoaderMetadataService(),
            NoOpJavaRuntimeResolver.Instance,
            NullRuntimeMetadataStore.Instance,
            skinLibraryRepository,
            accountAppearanceRepository,
            NullStructuredLogger.Instance,
            DefaultOperationContextFactory.Instance);
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

    private sealed class FakeSkinLibraryRepository : ISkinLibraryRepository
    {
        private readonly IReadOnlyList<SkinAssetSummary> skins;
        private readonly IReadOnlyList<CapeAssetSummary> capes;

        public FakeSkinLibraryRepository(SkinAssetSummary skin, CapeAssetSummary cape)
        {
            skins = [skin];
            capes = [cape];
        }

        public Task<IReadOnlyList<SkinAssetSummary>> ListSkinsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(skins);

        public Task<SkinAssetSummary?> GetSkinByIdAsync(string skinId, CancellationToken cancellationToken = default)
            => Task.FromResult<SkinAssetSummary?>(skins.FirstOrDefault(item => string.Equals(item.SkinId, skinId, StringComparison.Ordinal)));

        public Task SaveSkinAsync(SkinAssetSummary skin, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<CapeAssetSummary>> ListCapesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(capes);

        public Task<CapeAssetSummary?> GetCapeByIdAsync(string capeId, CancellationToken cancellationToken = default)
            => Task.FromResult<CapeAssetSummary?>(capes.FirstOrDefault(item => string.Equals(item.CapeId, capeId, StringComparison.Ordinal)));

        public Task SaveCapeAsync(CapeAssetSummary cape, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeAccountAppearanceRepository : IAccountAppearanceRepository
    {
        private readonly AccountAppearanceSelection? selection;

        public FakeAccountAppearanceRepository(AccountAppearanceSelection? selection)
        {
            this.selection = selection;
        }

        public Task<AccountAppearanceSelection?> GetAsync(AccountId accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(selection is not null && selection.AccountId == accountId ? selection : null);

        public Task SaveAsync(AccountAppearanceSelection selection, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
