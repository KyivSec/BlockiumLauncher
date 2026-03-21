using Xunit;
using BlockiumLauncher.Domain.Entities;

namespace BlockiumLauncher.Domain.Tests;

public sealed class LaunchProfileTests
{
    [Fact]
    public void Constructor_StoresValues()
    {
        var TestLaunchProfile = new LaunchProfile(
            MinMemoryMb: 2048,
            MaxMemoryMb: 4096,
            ExtraJvmArgs: ["-XX:+UseG1GC"],
            ExtraGameArgs: ["--demo"],
            EnvironmentVariables: [new KeyValuePair<string, string>("JAVA_HOME", @"C:\Java")]);

        Assert.Equal(2048, TestLaunchProfile.MinMemoryMb);
        Assert.Equal(4096, TestLaunchProfile.MaxMemoryMb);
        Assert.Single(TestLaunchProfile.ExtraJvmArgs);
        Assert.Single(TestLaunchProfile.ExtraGameArgs);
        Assert.Single(TestLaunchProfile.EnvironmentVariables);
    }

    [Fact]
    public void Constructor_MinGreaterThanMax_Throws()
    {
        Action Act = () => _ = new LaunchProfile(
            MinMemoryMb: 4096,
            MaxMemoryMb: 2048,
            ExtraJvmArgs: Array.Empty<string>(),
            ExtraGameArgs: Array.Empty<string>(),
            EnvironmentVariables: Array.Empty<KeyValuePair<string, string>>());

        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void Constructor_NonPositiveMemory_Throws()
    {
        Action Act = () => _ = new LaunchProfile(
            MinMemoryMb: 0,
            MaxMemoryMb: 2048,
            ExtraJvmArgs: Array.Empty<string>(),
            ExtraGameArgs: Array.Empty<string>(),
            EnvironmentVariables: Array.Empty<KeyValuePair<string, string>>());

        Assert.Throws<ArgumentOutOfRangeException>(Act);
    }

    [Fact]
    public void Constructor_NullCollections_Throws()
    {
        Action Act = () => _ = new LaunchProfile(
            MinMemoryMb: 1024,
            MaxMemoryMb: 2048,
            ExtraJvmArgs: null!,
            ExtraGameArgs: Array.Empty<string>(),
            EnvironmentVariables: Array.Empty<KeyValuePair<string, string>>());

        Assert.Throws<ArgumentNullException>(Act);
    }

    [Fact]
    public void Constructor_BlankEnvironmentKey_Throws()
    {
        Action Act = () => _ = new LaunchProfile(
            MinMemoryMb: 1024,
            MaxMemoryMb: 2048,
            ExtraJvmArgs: Array.Empty<string>(),
            ExtraGameArgs: Array.Empty<string>(),
            EnvironmentVariables: [new KeyValuePair<string, string>(" ", "Value")]);

        Assert.Throws<ArgumentException>(Act);
    }
}
