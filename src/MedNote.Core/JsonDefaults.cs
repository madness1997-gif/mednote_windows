using System.Text.Json;
using System.Text.Json.Serialization;

namespace MedNote.Core;

public static class JsonDefaults
{
    public static JsonSerializerOptions Create(bool writeIndented = false) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = writeIndented,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
        },
    };
}
