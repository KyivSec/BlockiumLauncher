using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Infrastructure.Storage;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Storage;

public sealed class InstanceContentInstallerTests
{
    [Fact]
    public async Task PrepareAsync_DelegatesToMatchingPreparer()
    {
        var factory = new TempWorkspaceFactory();
        await using var workspace = await factory.CreateAsync("content-installer");

        var plan = new InstallPlan
        {
            InstanceName = "TestInstance",
            GameVersion = "1.20.1",
            LoaderType = LoaderType.Vanilla,
            LoaderVersion = null,
            TargetDirectory = Path.Combine(Path.GetTempPath(), "UnusedTarget"),
            Steps = []
        };

        var installer = new InstanceContentInstaller(
        [
            new TestLoaderRuntimePreparer(
                LoaderType.Vanilla,
                global::BlockiumLauncher.Shared.Results.Result<string>.Success(workspace.RootPath))
        ]);

        var prepareResult = await installer.PrepareAsync(plan, workspace);

        Assert.True(prepareResult.IsSuccess);
        Assert.Equal(workspace.RootPath, prepareResult.Value);
    }

    [Fact]
    public async Task PrepareAsync_FailsWhenNoMatchingPreparerExists()
    {
        var factory = new TempWorkspaceFactory();
        await using var workspace = await factory.CreateAsync("content-installer");

        var plan = new InstallPlan
        {
            InstanceName = "TestInstance",
            GameVersion = "1.20.1",
            LoaderType = LoaderType.Fabric,
            LoaderVersion = "0.16.10",
            TargetDirectory = Path.Combine(Path.GetTempPath(), "UnusedTarget"),
            Steps = []
        };

        var installer = new InstanceContentInstaller(
        [
            new TestLoaderRuntimePreparer(
                LoaderType.Vanilla,
                global::BlockiumLauncher.Shared.Results.Result<string>.Success(workspace.RootPath))
        ]);

        var prepareResult = await installer.PrepareAsync(plan, workspace);

        Assert.True(prepareResult.IsFailure);
        Assert.Equal("Install.LoaderRuntimePreparerNotFound", prepareResult.Error.Code);
    }

    private sealed class TestLoaderRuntimePreparer : ILoaderRuntimePreparer
    {
        private readonly LoaderType loaderType;
        private readonly global::BlockiumLauncher.Shared.Results.Result<string> result;

        public TestLoaderRuntimePreparer(
            LoaderType loaderType,
            global::BlockiumLauncher.Shared.Results.Result<string> result)
        {
            this.loaderType = loaderType;
            this.result = result;
        }

        public bool CanPrepare(LoaderType loaderType)
        {
            return loaderType == this.loaderType;
        }

        public Task<global::BlockiumLauncher.Shared.Results.Result<string>> PrepareAsync(
            InstallPlan plan,
            ITempWorkspace workspace,
            IProgress<InstallPreparationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }
}
