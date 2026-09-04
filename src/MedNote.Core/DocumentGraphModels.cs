using System.Text.Json;
using System.Text.Json.Serialization;

namespace MedNote.Core;

public sealed record DocumentRecord
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string Name { get; init; } = string.Empty;

    [JsonRequired]
    public long Size { get; init; }

    [JsonRequired]
    public long LastModified { get; init; }

    [JsonRequired]
    public bool Available { get; init; }

    [JsonRequired]
    public JsonElement Payload { get; init; } = JsonValues.EmptyObject();

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record DocumentContextRecord
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string Kind { get; init; } = string.Empty;

    [JsonRequired]
    public string Name { get; init; } = string.Empty;

    [JsonRequired]
    public List<string> DocumentIds { get; init; } = [];

    [JsonRequired]
    public string? ActiveDocumentId { get; init; }

    [JsonRequired]
    public int SourcePage { get; init; } = 1;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record DocumentGroupRecord
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string Name { get; init; } = string.Empty;

    [JsonRequired]
    public List<string> DocumentIds { get; init; } = [];

    [JsonRequired]
    public long CreatedAt { get; init; }

    [JsonRequired]
    public long UpdatedAt { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public enum DocumentLinkTargetType
{
    Page,
    Sheet,
}

public enum DocumentRelationKind
{
    Workspace,
    Content,
}

public enum DocumentRelationSourceType
{
    Document,
    Group,
}

public enum WorkspaceMode
{
    Split,
    Reader,
    Note,
}

public sealed record NoteDocumentLink
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public string DocumentId { get; init; } = string.Empty;

    [JsonRequired]
    public DocumentLinkTargetType TargetType { get; init; }

    [JsonRequired]
    public string TargetId { get; init; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record DocumentWorkspacePreset
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkspaceMode? WorkspaceMode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ReaderShare { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? NoteZoom { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActiveDocumentId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, int>? PdfPages { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record DocumentContentAnchor
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DocumentId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PdfPage { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Rect { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AnnotationId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Quote { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record DocumentLinkRelation
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    public List<string> LinkIds { get; init; } = [];

    [JsonRequired]
    public DocumentRelationKind Kind { get; init; }

    [JsonRequired]
    public DocumentRelationSourceType SourceType { get; init; }

    [JsonRequired]
    public string SourceId { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyTargetType { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyTargetId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsDefault { get; init; }

    [JsonRequired]
    public long CreatedAt { get; init; }

    [JsonRequired]
    public long UpdatedAt { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? LastOpenedAt { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DocumentWorkspacePreset? WorkspacePreset { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DocumentContentAnchor? ContentAnchor { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record DocumentGraph
{
    [JsonRequired]
    public List<DocumentRecord> Documents { get; init; } = [];

    [JsonRequired]
    public List<DocumentContextRecord> Contexts { get; init; } = [];

    [JsonRequired]
    public List<DocumentGroupRecord> Groups { get; init; } = [];

    [JsonRequired]
    public List<NoteDocumentLink> Links { get; init; } = [];

    [JsonRequired]
    public List<DocumentLinkRelation> LinkRelations { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}
