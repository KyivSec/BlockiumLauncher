using Xunit;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Domain.Tests;

public sealed class LauncherInstanceTests
{
    [Fact]
    public void Create_VanillaWithLoaderVersion_Throws()
    {
        Action Act = () => _ = LauncherInstance.Create(
            InstanceId.New(),
            "Test",
            new VersionId("1.21.1"),
            LoaderType.Vanilla,
            new VersionId("loader"),
            @"C:\Blockium\Test",
            DateTimeOffset.UtcNow,
            LaunchProfile.CreateDefault());

        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void Create_ModdedWithoutLoaderVersion_Throws()
    {
        Action Act = () => _ = LauncherInstance.Create(
            InstanceId.New(),
            "Test",
            new VersionId("1.21.1"),
            LoaderType.Forge,
            null,
            @"C:\Blockium\Test",
            DateTimeOffset.UtcNow,
            LaunchProfile.CreateDefault());

        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void Create_BlankName_Throws()
    {
        Action Act = () => _ = LauncherInstance.Create(
            InstanceId.New(),
            " ",
            new VersionId("1.21.1"),
            LoaderType.Vanilla,
            null,
            @"C:\Blockium\Test",
            DateTimeOffset.UtcNow,
            LaunchProfile.CreateDefault());

        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void Create_BlankInstallLocation_Throws()
    {
        Action Act = () => _ = LauncherInstance.Create(
            InstanceId.New(),
            "Test",
            new VersionId("1.21.1"),
            LoaderType.Vanilla,
            null,
            " ",
            DateTimeOffset.UtcNow,
            LaunchProfile.CreateDefault());

        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void DeletedInstance_CannotTransitionBack()
    {
        var TestLauncherInstance = LauncherInstance.Create(
            InstanceId.New(),
            "Test",
            new VersionId("1.21.1"),
            LoaderType.Vanilla,
            null,
            @"C:\Blockium\Test",
            DateTimeOffset.UtcNow,
            LaunchProfile.CreateDefault());

        TestLauncherInstance.MarkDeleted();

        Action Act = () => TestLauncherInstance.MarkInstalled();

        Assert.Throws<InvalidOperationException>(Act);
    }

    [Fact]
    public void RecordLaunch_UpdatesLastPlayedAtUtc_WhenInstalled()
    {
        var TestLauncherInstance = LauncherInstance.Create(
            InstanceId.New(),
            "Test",
            new VersionId("1.21.1"),
            LoaderType.Vanilla,
            null,
            @"C:\Blockium\Test",
            DateTimeOffset.UtcNow,
            LaunchProfile.CreateDefault());

        var TimestampUtc = DateTimeOffset.UtcNow;

        TestLauncherInstance.MarkInstalled();
        TestLauncherInstance.RecordLaunch(TimestampUtc);

        Assert.Equal(TimestampUtc, TestLauncherInstance.LastPlayedAtUtc);
    }

    [Fact]
    public void RecordLaunch_FailsWhenBroken()
    {
        var TestLauncherInstance = LauncherInstance.Create(
            InstanceId.New(),
            "Test",
            new VersionId("1.21.1"),
            LoaderType.Vanilla,
            null,
            @"C:\Blockium\Test",
            DateTimeOffset.UtcNow,
            LaunchProfile.CreateDefault());

        TestLauncherInstance.MarkBroken();

        Action Act = () => TestLauncherInstance.RecordLaunch(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(Act);
    }

    [Fact]
    public void RecordLaunch_FailsWhenDeleted()
    {
        var TestLauncherInstance = LauncherInstance.Create(
            InstanceId.New(),
            "Test",
            new VersionId("1.21.1"),
            LoaderType.Vanilla,
            null,
            @"C:\Blockium\Test",
            DateTimeOffset.UtcNow,
            LaunchProfile.CreateDefault());

        TestLauncherInstance.MarkDeleted();

        Action Act = () => TestLauncherInstance.RecordLaunch(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(Act);
    }
}
