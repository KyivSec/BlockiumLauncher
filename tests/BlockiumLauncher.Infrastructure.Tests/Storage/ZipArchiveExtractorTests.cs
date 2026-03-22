using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using BlockiumLauncher.Infrastructure.Storage;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Storage;

public sealed class ZipArchiveExtractorTests
{
    [Fact]
    public async Task ExtractAsync_ExtractsNormalArchive()
    {
        var TempRoot = Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(TempRoot);

        try
        {
            var ArchivePath = Path.Combine(TempRoot, "test.zip");
            var DestinationPath = Path.Combine(TempRoot, "out");

            using (var Archive = ZipFile.Open(ArchivePath, ZipArchiveMode.Create))
            {
                var Entry = Archive.CreateEntry("folder/file.txt");
                await using var Stream = Entry.Open();
                await using var Writer = new StreamWriter(Stream);
                await Writer.WriteAsync("hello");
            }

            var Extractor = new ZipArchiveExtractor();
            var Result = await Extractor.ExtractAsync(ArchivePath, DestinationPath);

            Assert.True(Result.IsSuccess);
            Assert.True(File.Exists(Path.Combine(DestinationPath, "folder", "file.txt")));
            Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(DestinationPath, "folder", "file.txt")));
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
    public async Task ExtractAsync_RejectsZipSlip()
    {
        var TempRoot = Path.Combine(Path.GetTempPath(), "BlockiumLauncher.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(TempRoot);

        try
        {
            var ArchivePath = Path.Combine(TempRoot, "test.zip");
            var DestinationPath = Path.Combine(TempRoot, "out");

            using (var Archive = ZipFile.Open(ArchivePath, ZipArchiveMode.Create))
            {
                var Entry = Archive.CreateEntry("../evil.txt");
                await using var Stream = Entry.Open();
                await using var Writer = new StreamWriter(Stream);
                await Writer.WriteAsync("bad");
            }

            var Extractor = new ZipArchiveExtractor();
            var Result = await Extractor.ExtractAsync(ArchivePath, DestinationPath);

            Assert.True(Result.IsFailure);
            Assert.False(File.Exists(Path.Combine(TempRoot, "evil.txt")));
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