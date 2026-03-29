using System.Text.Json;
using System.Text.Json.Serialization;

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

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };
}

public sealed class JsonFileStore
{
    private readonly JsonSerializerOptions SerializerOptions;

    public JsonFileStore(JsonSerializerOptions? SerializerOptions = null)
    {
        this.SerializerOptions = SerializerOptions ?? JsonDefaults.SerializerOptions;
    }

    public async Task<T?> ReadOptionalAsync<T>(string FilePath, CancellationToken CancellationToken)
    {
        if (!File.Exists(FilePath)) {
            return default;
        }

        await using var Stream = File.OpenRead(FilePath);
        return await JsonSerializer.DeserializeAsync<T>(Stream, SerializerOptions, CancellationToken);
    }

    public async Task<T> ReadRequiredAsync<T>(string FilePath, CancellationToken CancellationToken)
    {
        if (!File.Exists(FilePath)) {
            throw new FileNotFoundException("Required JSON file was not found.", FilePath);
        }

        await using var Stream = File.OpenRead(FilePath);
        var Value = await JsonSerializer.DeserializeAsync<T>(Stream, SerializerOptions, CancellationToken);

        if (Value is null) {
            throw new InvalidDataException($"File '{FilePath}' contained null JSON content.");
        }

        return Value;
    }

    public async Task WriteAsync<T>(string FilePath, T Value, CancellationToken CancellationToken)
    {
        var Json = JsonSerializer.Serialize(Value, SerializerOptions);
        await AtomicFileWriter.WriteAllTextAsync(FilePath, Json, CancellationToken);
    }
}
