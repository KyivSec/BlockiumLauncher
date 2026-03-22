using System.IO;
using System.Threading.Tasks;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Infrastructure.Storage;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Storage;

public sealed class InstanceContentInstallerTests
{
    [Fact]
    public async Task PrepareAsync_CreatesDirectoriesAndMetadata()
    {
        var Factory = new TempWorkspaceFactory();
        await using var Workspace = await Factory.CreateAsync("content-installer");

        var Plan = new InstallPlan
        {
            InstanceName = "TestInstance",
            GameVersion = "1.20.1",
            LoaderType = LoaderType.Vanilla,
            LoaderVersion = null,
            TargetDirectory = Path.Combine(Path.GetTempPath(), "UnusedTarget"),
            Steps =
            [
                new InstallPlanStep
                {
                    Kind = InstallPlanStepKind.CreateDirectory,
                    Destination = ".minecraft"
                },
                new InstallPlanStep
                {
                    Kind = InstallPlanStepKind.CreateDirectory,
                    Destination = ".minecraft\\mods"
                },
                new InstallPlanStep
                {
                    Kind = InstallPlanStepKind.WriteMetadata,
                    Destination = ".blockium\\install-plan.json"
                }
            ]
        };

        var Installer = new InstanceContentInstaller();
        var Result = await Installer.PrepareAsync(Plan, Workspace);

        Assert.True(Result.IsSuccess);

        var RootPath = Result.Value;
        Assert.True(Directory.Exists(Path.Combine(RootPath, ".minecraft")));
        Assert.True(Directory.Exists(Path.Combine(RootPath, ".minecraft", "mods")));
        Assert.True(File.Exists(Path.Combine(RootPath, ".blockium", "install-plan.json")));
        Assert.True(File.Exists(Path.Combine(RootPath, "instance.json")));
    }
}