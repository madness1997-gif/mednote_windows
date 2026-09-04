using System.Text.Json;
using System.Text.Json.Serialization;

namespace MedNote.Core.Compatibility.WebV6;

public static class WebV6Schema
{
    public const int Version = 6;

    public const string DriveBackupFormat = "mednote-library-v2";

    public const string SheetHashAlgorithm = "json-safe-v1";
}

public sealed record WebLibraryV6
{
    [JsonRequired]
    public int Version { get; init; } = WebV6Schema.Version;

    [JsonRequired]
    public NoteStructure Notes { get; init; } = new();

    [JsonRequired]
    public Dictionary<string, JsonElement> SheetContents { get; init; } = [];

    [JsonRequired]
    public DocumentGraph Documents { get; init; } = new();

    [JsonRequired]
    public LibraryPreferences Preferences { get; init; } = new();

    [JsonRequired]
    public long SavedAt { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public interface IWebV6SheetContentConverter
{
    ValueTask<RtfSheetContent> ConvertAsync(
        string sheetId,
        JsonElement webContent,
        CancellationToken cancellationToken = default);
}
