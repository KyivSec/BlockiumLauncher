using System.IO;
using System.Threading.Tasks;
using BlockiumLauncher.Infrastructure.Storage;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Storage;

public sealed class TempWorkspaceFactoryTests
{
    [Fact]
    public async Task CreateAsync_CreatesUniqueWorkspace_AndDisposeDeletesIt()
    {
        var Factory = new TempWorkspaceFactory();

        var Workspace = await Factory.CreateAsync("test");
        var RootPath = Workspace.RootPath;

        Assert.True(Directory.Exists(RootPath));

        await Workspace.DisposeAsync();

        Assert.False(Directory.Exists(RootPath));
    }
}