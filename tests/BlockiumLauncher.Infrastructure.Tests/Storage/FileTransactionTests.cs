using System.IO;
using System.Threading.Tasks;
using BlockiumLauncher.Infrastructure.Storage;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Storage;

public sealed class FileTransactionTests
{
    [Fact]
    public async Task CommitAsync_MovesStagedContentIntoTarget()
    {
        var TempRoot = Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(TempRoot);

        try
        {
            var SourcePath = Path.Combine(TempRoot, "source");
            var TargetPath = Path.Combine(TempRoot, "target");
            Directory.CreateDirectory(SourcePath);
            await File.WriteAllTextAsync(Path.Combine(SourcePath, "file.txt"), "abc");

            await using var Transaction = new FileTransaction();

            var BeginResult = await Transaction.BeginAsync(TargetPath);
            var StageResult = await Transaction.StageDirectoryAsync(SourcePath);
            var CommitResult = await Transaction.CommitAsync();

            Assert.True(BeginResult.IsSuccess);
            Assert.True(StageResult.IsSuccess);
            Assert.True(CommitResult.IsSuccess);
            Assert.True(File.Exists(Path.Combine(TargetPath, "file.txt")));
            Assert.Equal("abc", await File.ReadAllTextAsync(Path.Combine(TargetPath, "file.txt")));
        }
        finally
        {
            if (Directory.Exists(TempRoot))
            {
                Directory.Delete(TempRoot, true);
            }
        }
    }

    [Fact]
    public async Task RollbackAsync_RemovesPartialState()
    {
        var TempRoot = Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(TempRoot);

        try
        {
            var SourcePath = Path.Combine(TempRoot, "source");
            var TargetPath = Path.Combine(TempRoot, "target");
            Directory.CreateDirectory(SourcePath);
            await File.WriteAllTextAsync(Path.Combine(SourcePath, "file.txt"), "abc");

            await using var Transaction = new FileTransaction();

            await Transaction.BeginAsync(TargetPath);
            await Transaction.StageDirectoryAsync(SourcePath);
            var RollbackResult = await Transaction.RollbackAsync();

            Assert.True(RollbackResult.IsSuccess);
            Assert.False(Directory.Exists(TargetPath));
        }
        finally
        {
            if (Directory.Exists(TempRoot))
            {
                Directory.Delete(TempRoot, true);
            }
        }
    }
}