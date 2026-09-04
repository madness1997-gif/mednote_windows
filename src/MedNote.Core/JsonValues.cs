using System.Text.Json;

namespace MedNote.Core;

public static class JsonValues
{
    public static JsonElement EmptyObject() => JsonSerializer.SerializeToElement(new Dictionary<string, object?>());

    public static JsonElement Object(params (string Name, object? Value)[] properties) =>
        JsonSerializer.SerializeToElement(properties.ToDictionary(item => item.Name, item => item.Value));
}
