using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlockiumLauncher.Infrastructure.Persistence.Json;

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
