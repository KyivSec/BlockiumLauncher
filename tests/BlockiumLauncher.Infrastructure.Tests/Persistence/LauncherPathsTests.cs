using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Persistence;

public sealed class LauncherPathsTests
{
    [Fact]
    public void BuildsDeterministicPaths()
    {
        var Paths = new LauncherPaths(@"C:\Temp\BlockiumLauncher");

        Assert.Equal(@"C:\Temp\BlockiumLauncher\data\instances.json", Paths.InstancesFilePath);
        Assert.Equal(@"C:\Temp\BlockiumLauncher\data\accounts.json", Paths.AccountsFilePath);
        Assert.Equal(@"C:\Temp\BlockiumLauncher\data\java-installations.json", Paths.JavaInstallationsFilePath);
        Assert.Equal(@"C:\Temp\BlockiumLauncher\cache\versions.json", Paths.VersionsCacheFilePath);
    }

    [Fact]
    public void BuildsLoaderCachePath()
    {
        var Paths = new LauncherPaths(@"C:\Temp\BlockiumLauncher");
        var FilePath = Paths.GetLoaderVersionsCacheFilePath(LoaderType.Forge, new VersionId("1.21.1"));

        Assert.Equal(@"C:\Temp\BlockiumLauncher\cache\loaders\forge-1.21.1.json", FilePath);
    }
}
