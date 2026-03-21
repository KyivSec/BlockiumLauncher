namespace BlockiumLauncher.Infrastructure.Persistence.Json;

public static class AtomicFileWriter
{
    public static async Task WriteAllTextAsync(string FilePath, string Content, CancellationToken CancellationToken)
    {
        var DirectoryPath = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrWhiteSpace(DirectoryPath)) {
            throw new InvalidOperationException("Target file path must include a directory.");
        }

        Directory.CreateDirectory(DirectoryPath);

        var TempFilePath = Path.Combine(
            DirectoryPath,
            $"{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");

        await File.WriteAllTextAsync(TempFilePath, Content, CancellationToken);

        if (File.Exists(FilePath)) {
            File.Delete(FilePath);
        }

        File.Move(TempFilePath, FilePath);
    }
}
