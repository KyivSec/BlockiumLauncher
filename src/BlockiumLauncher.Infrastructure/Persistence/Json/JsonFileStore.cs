using System.Text.Json;

namespace BlockiumLauncher.Infrastructure.Persistence.Json;

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
