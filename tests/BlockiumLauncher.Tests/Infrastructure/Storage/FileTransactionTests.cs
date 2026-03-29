using System;
using System.IO;
using System.Threading.Tasks;
using BlockiumLauncher.Infrastructure.Storage;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Storage;

public sealed class FileTransactionTests
{
    [Fact]
    public async Task CommitAsync_MovesStagedDirectory_WhenTargetDoesNotExist()
    {
        var RootPath = CreateDirectory();
        var StagedPath = Path.Combine(RootPath, "staged");
        var TargetPath = Path.Combine(RootPath, "target");

        Directory.CreateDirectory(StagedPath);
        File.WriteAllText(Path.Combine(StagedPath, "marker.txt"), "stage");

        try
        {
            await using var Transaction = new FileTransaction();

            var BeginResult = await Transaction.BeginAsync(TargetPath);
            var StageResult = await Transaction.StageDirectoryAsync(StagedPath);
            var CommitResult = await Transaction.CommitAsync();

            Assert.True(BeginResult.IsSuccess);
            Assert.True(StageResult.IsSuccess);
            Assert.True(CommitResult.IsSuccess);
            Assert.False(Directory.Exists(StagedPath));
            Assert.True(Directory.Exists(TargetPath));
            Assert.True(File.Exists(Path.Combine(TargetPath, "marker.txt")));
        }
        finally
        {
            DeleteDirectoryIfExists(RootPath);
        }
    }

    [Fact]
    public async Task CommitAsync_ReplacesExistingTarget_WhenTargetAlreadyExists()
    {
        var RootPath = CreateDirectory();
        var StagedPath = Path.Combine(RootPath, "staged");
        var TargetPath = Path.Combine(RootPath, "target");

        Directory.CreateDirectory(StagedPath);
        File.WriteAllText(Path.Combine(StagedPath, "new.txt"), "new");

        Directory.CreateDirectory(TargetPath);
        File.WriteAllText(Path.Combine(TargetPath, "old.txt"), "old");

        try
        {
            await using var Transaction = new FileTransaction();

            var BeginResult = await Transaction.BeginAsync(TargetPath);
            var StageResult = await Transaction.StageDirectoryAsync(StagedPath);
            var CommitResult = await Transaction.CommitAsync();

            Assert.True(BeginResult.IsSuccess);
            Assert.True(StageResult.IsSuccess);
            Assert.True(CommitResult.IsSuccess);
            Assert.False(Directory.Exists(StagedPath));
            Assert.True(Directory.Exists(TargetPath));
            Assert.True(File.Exists(Path.Combine(TargetPath, "new.txt")));
            Assert.False(File.Exists(Path.Combine(TargetPath, "old.txt")));
            Assert.False(Directory.Exists(TargetPath + ".__blockium_backup__"));
        }
        finally
        {
            DeleteDirectoryIfExists(RootPath);
        }
    }

    [Fact]
    public async Task StageDirectoryAsync_ReturnsFailure_WhenBeginWasNotCalled()
    {
        var RootPath = CreateDirectory();
        var StagedPath = Path.Combine(RootPath, "staged");
        Directory.CreateDirectory(StagedPath);

        try
        {
            await using var Transaction = new FileTransaction();

            var StageResult = await Transaction.StageDirectoryAsync(StagedPath);

            Assert.True(StageResult.IsFailure);
        }
        finally
        {
            DeleteDirectoryIfExists(RootPath);
        }
    }

    [Fact]
    public async Task CommitAsync_ReturnsFailure_WhenStagedDirectoryDoesNotExist()
    {
        var RootPath = CreateDirectory();
        var StagedPath = Path.Combine(RootPath, "missing");
        var TargetPath = Path.Combine(RootPath, "target");

        try
        {
            await using var Transaction = new FileTransaction();

            var BeginResult = await Transaction.BeginAsync(TargetPath);
            var StageResult = await Transaction.StageDirectoryAsync(StagedPath);

            Assert.True(BeginResult.IsSuccess);
            Assert.True(StageResult.IsFailure);
            Assert.False(Directory.Exists(TargetPath));
        }
        finally
        {
            DeleteDirectoryIfExists(RootPath);
        }
    }

    [Fact]
    public async Task RollbackAsync_DeletesStagedDirectory_BeforeCommit()
    {
        var RootPath = CreateDirectory();
        var StagedPath = Path.Combine(RootPath, "staged");
        var TargetPath = Path.Combine(RootPath, "target");

        Directory.CreateDirectory(StagedPath);
        File.WriteAllText(Path.Combine(StagedPath, "marker.txt"), "stage");

        try
        {
            await using var Transaction = new FileTransaction();

            var BeginResult = await Transaction.BeginAsync(TargetPath);
            var StageResult = await Transaction.StageDirectoryAsync(StagedPath);
            var RollbackResult = await Transaction.RollbackAsync();

            Assert.True(BeginResult.IsSuccess);
            Assert.True(StageResult.IsSuccess);
            Assert.True(RollbackResult.IsSuccess);
            Assert.False(Directory.Exists(StagedPath));
            Assert.False(Directory.Exists(TargetPath));
        }
        finally
        {
            DeleteDirectoryIfExists(RootPath);
        }
    }

    private static string CreateDirectory()
    {
        var PathValue = Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(PathValue);
        return PathValue;
    }

    private static void DeleteDirectoryIfExists(string PathValue)
    {
        if (Directory.Exists(PathValue))
        {
            Directory.Delete(PathValue, true);
        }
    }
}