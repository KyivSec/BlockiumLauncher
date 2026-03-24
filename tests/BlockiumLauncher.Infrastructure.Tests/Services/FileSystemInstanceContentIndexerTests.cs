using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Infrastructure.Services;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Services;

public sealed class FileSystemInstanceContentIndexerTests
{
    [Fact]
    public async Task ScanAsync_IndexesContentAndPreservesSourceMetadata()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(rootDirectory, ".minecraft", "mods"));
        Directory.CreateDirectory(Path.Combine(rootDirectory, ".minecraft", "resourcepacks"));
        Directory.CreateDirectory(Path.Combine(rootDirectory, ".minecraft", "shaderpacks"));
        Directory.CreateDirectory(Path.Combine(rootDirectory, ".minecraft", "saves", "WorldOne"));
        Directory.CreateDirectory(Path.Combine(rootDirectory, ".minecraft", "screenshots"));
        Directory.CreateDirectory(Path.Combine(rootDirectory, ".blockium"));

        await File.WriteAllTextAsync(Path.Combine(rootDirectory, ".minecraft", "mods", "enabled.jar"), "mod");
        await File.WriteAllTextAsync(Path.Combine(rootDirectory, ".minecraft", "mods", "disabled.jar.disabled"), "mod");
        await File.WriteAllTextAsync(Path.Combine(rootDirectory, ".minecraft", "resourcepacks", "pack.zip"), "pack");
        await File.WriteAllTextAsync(Path.Combine(rootDirectory, ".minecraft", "shaderpacks", "shader.zip"), "shader");
        await File.WriteAllTextAsync(Path.Combine(rootDirectory, ".minecraft", "screenshots", "screen.png"), "shot");
        await File.WriteAllTextAsync(Path.Combine(rootDirectory, ".minecraft", "servers.dat"), "servers");
        await File.WriteAllTextAsync(Path.Combine(rootDirectory, ".blockium", "icon.png"), "icon");

        var existingMetadata = new InstanceContentMetadata
        {
            Mods =
            [
                new InstanceFileMetadata
                {
                    Name = "enabled.jar",
                    RelativePath = ".minecraft/mods/enabled.jar",
                    AbsolutePath = Path.Combine(rootDirectory, ".minecraft", "mods", "enabled.jar"),
                    Source = new ContentSourceMetadata
                    {
                        Provider = ContentOriginProvider.Modrinth,
                        ContentType = "mod",
                        ProjectId = "project-1"
                    }
                }
            ]
        };

        try
        {
            var indexer = new FileSystemInstanceContentIndexer();
            var metadata = await indexer.ScanAsync(rootDirectory, existingMetadata);

            Assert.Equal(Path.Combine(rootDirectory, ".blockium", "icon.png"), metadata.IconPath);
            Assert.Equal(2, metadata.Mods.Count);
            Assert.Contains(metadata.Mods, item => item.Name == "disabled.jar.disabled" && item.IsDisabled);
            Assert.Single(metadata.ResourcePacks);
            Assert.Single(metadata.Shaders);
            Assert.Single(metadata.Worlds);
            Assert.Single(metadata.Screenshots);
            Assert.Single(metadata.Servers);
            Assert.Equal(ContentOriginProvider.Modrinth, metadata.Mods.First(item => item.Name == "enabled.jar").Source!.Provider);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, true);
            }
        }
    }
}
