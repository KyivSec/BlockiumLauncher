using BlockiumLauncher.Application.UseCases.Instances;
using BlockiumLauncher.Application.UseCases.Launch;
using BlockiumLauncher.Application.UseCases.Metadata;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using Xunit;

namespace BlockiumLauncher.Application.Tests.Abstractions;

public sealed class RequestContractTests
{
    [Fact]
    public void CreateInstanceRequest_TrimsInput()
    {
        var Request = new CreateInstanceRequest(
            "  Test Instance  ",
            new VersionId("1.21.1"),
            LoaderType.Vanilla,
            null,
            @"  C:\Instances\Test  ");

        Assert.Equal("Test Instance", Request.Name);
        Assert.Equal("1.21.1", Request.GameVersion.ToString());
        Assert.Equal(@"C:\Instances\Test", Request.InstallLocation);
    }

    [Fact]
    public void CreateInstanceRequest_RejectsBlankName()
    {
        var Action = () => new CreateInstanceRequest(
            "   ",
            new VersionId("1.21.1"),
            LoaderType.Vanilla,
            null,
            @"C:\Instances\Test");

        Assert.Throws<ArgumentException>(Action);
    }

    [Fact]
    public void ResolveInstallPlanRequest_RejectsLoaderVersionForVanilla()
    {
        var Action = () => new ResolveInstallPlanRequest(
            new VersionId("1.21.1"),
            LoaderType.Vanilla,
            "0.1.0");

        Assert.Throws<ArgumentException>(Action);
    }

    [Fact]
    public void ResolveInstallPlanRequest_RequiresLoaderVersionForForge()
    {
        var Action = () => new ResolveInstallPlanRequest(
            new VersionId("1.21.1"),
            LoaderType.Forge,
            null);

        Assert.Throws<ArgumentException>(Action);
    }

    [Fact]
    public void TailLogRequest_RejectsNonPositiveMaxLines()
    {
        var Action = () => new TailLogRequest(InstanceId.New(), 0);

        Assert.Throws<ArgumentOutOfRangeException>(Action);
    }
}
