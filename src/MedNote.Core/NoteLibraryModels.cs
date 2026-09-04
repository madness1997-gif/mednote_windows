using System.Text.Json;
using System.Text.Json.Serialization;

namespace MedNote.Core;

public static class NativeNoteSchema
{
    public const int Version = 1;

    public const string RtfContentFormat = "rtf";
}

public static class RtfDocument
{
    public const string Empty = @"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\viewkind4\uc1\pard\f0\fs24 }";

    public static bool IsRtf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var span = value.AsSpan();
        var index = 0;
        while (index < span.Length && (char.IsWhiteSpace(span[index]) || span[index] == '\uFEFF'))
        {
            index++;
        }

        return value[index..].StartsWith(@"{\rtf", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record WorkspaceRecord
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string Title { get; init; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record NotebookRecord
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string Title { get; init; } = string.Empty;

    [JsonRequired]
    public int Order { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record SectionRecord
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string NotebookId { get; init; } = string.Empty;

    [JsonRequired]
    public string Title { get; init; } = string.Empty;

    [JsonRequired]
    public int Order { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record PageRecord
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string SectionId { get; init; } = string.Empty;

    [JsonRequired]
    public string Title { get; init; } = string.Empty;

    [JsonRequired]
    public int Order { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record SheetRecord
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string PageId { get; init; } = string.Empty;

    [JsonRequired]
    public int Order { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record ActiveNoteState
{
    [JsonRequired]
    public string ActiveNotebookId { get; init; } = string.Empty;

    [JsonRequired]
    public string ActiveSectionId { get; init; } = string.Empty;

    [JsonRequired]
    public string ActivePageId { get; init; } = string.Empty;

    [JsonRequired]
    public string ActiveSheetId { get; init; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];

    public static ActiveNoteState Empty { get; } = new();
}

public sealed record NoteStructure
{
    [JsonRequired]
    public WorkspaceRecord Workspace { get; init; } = new();

    [JsonRequired]
    public List<NotebookRecord> Notebooks { get; init; } = [];

    [JsonRequired]
    public List<SectionRecord> Sections { get; init; } = [];

    [JsonRequired]
    public List<PageRecord> Pages { get; init; } = [];

    [JsonRequired]
    public List<SheetRecord> Sheets { get; init; } = [];

    [JsonRequired]
    public ActiveNoteState Active { get; init; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record RtfSheetContent
{
    [JsonRequired]
    public string Rtf { get; init; } = RtfDocument.Empty;

    public static RtfSheetContent CreateEmpty() => new();
}

public sealed record HydratedSheet
{
    public SheetRecord Sheet { get; init; } = new();

    public RtfSheetContent Content { get; init; } = RtfSheetContent.CreateEmpty();
}

public sealed record LibraryPreferences
{
    [JsonRequired]
    public string ActiveDocumentContextId { get; init; } = string.Empty;

    [JsonRequired]
    public double ReaderShare { get; init; } = 50d;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkspaceMode? WorkspaceMode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? NoteZoom { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record LibraryRuntimeMetadata
{
    public LibraryPreferences Preferences { get; init; } = new();

    public long SavedAt { get; init; }
}

public sealed record NativeLibrarySnapshot
{
    [JsonRequired]
    public int Version { get; init; } = NativeNoteSchema.Version;

    [JsonRequired]
    public NoteStructure Notes { get; init; } = new();

    [JsonRequired]
    public Dictionary<string, RtfSheetContent> SheetContents { get; init; } = [];

    [JsonRequired]
    public DocumentGraph Documents { get; init; } = new();

    [JsonRequired]
    public LibraryPreferences Preferences { get; init; } = new();

    [JsonRequired]
    public long SavedAt { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}
