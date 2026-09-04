using System.Text.Json;

namespace MedNote.Core;

/// <summary>
/// Projects Reader state from the shared native Document graph and merges it
/// back without replacing Note-owned links, relations or future graph fields.
/// </summary>
public static class ReaderDocumentGraphBridge
{
    private static readonly JsonSerializerOptions Json = JsonDefaults.Create();
    private static readonly HashSet<string> OwnedPayloadFields = new(StringComparer.Ordinal)
    {
        "reader",
        "localPath",
        "position",
    };

    public static ReaderLibrary Read(DocumentGraph graph, long savedAt = 0)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var documents = graph.Documents
            .Where(HasReaderPayload)
            .Select(ToReaderDocument)
            .ToList();
        var documentIds = documents.Select(document => document.Id).ToHashSet(StringComparer.Ordinal);
        var context = graph.Contexts.FirstOrDefault(item => item.Id == ReaderV1Migration.ContextId);
        var activeDocumentId = context?.ActiveDocumentId is { } active && documentIds.Contains(active)
            ? active
            : documents.FirstOrDefault()?.Id;
        return new ReaderLibrary
        {
            Documents = documents,
            ActiveDocumentId = activeDocumentId,
            SavedAt = savedAt,
        };
    }

    public static DocumentGraph Merge(DocumentGraph graph, ReaderLibrary library)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(library);
        var existing = graph.Documents.ToDictionary(document => document.Id, StringComparer.Ordinal);
        var documents = graph.Documents.ToList();
        foreach (var readerDocument in library.Documents)
        {
            existing.TryGetValue(readerDocument.Id, out var previous);
            var nativeDocument = ToNativeDocument(readerDocument, previous);
            var index = documents.FindIndex(document => document.Id == nativeDocument.Id);
            if (index < 0)
            {
                documents.Add(nativeDocument);
            }
            else
            {
                documents[index] = nativeDocument;
            }
        }

        var readerIds = library.Documents.Select(document => document.Id).ToList();
        var activeDocumentId = library.ActiveDocumentId is { } active && readerIds.Contains(active, StringComparer.Ordinal)
            ? active
            : readerIds.FirstOrDefault();
        var previousContext = graph.Contexts.FirstOrDefault(context => context.Id == ReaderV1Migration.ContextId);
        var contexts = graph.Contexts.Where(context => context.Id != ReaderV1Migration.ContextId).ToList();
        if (activeDocumentId is not null)
        {
            var activeReader = library.Documents.First(document => document.Id == activeDocumentId);
            contexts.Add(new DocumentContextRecord
            {
                Id = ReaderV1Migration.ContextId,
                Kind = readerIds.Count > 1 ? "collection" : "document",
                Name = "Reader",
                DocumentIds = readerIds,
                ActiveDocumentId = activeDocumentId,
                SourcePage = Math.Max(1, activeReader.Reader.Page),
                ExtensionData = previousContext?.ExtensionData ?? [],
            });
        }

        return graph with { Documents = documents, Contexts = contexts };
    }

    private static bool HasReaderPayload(DocumentRecord document) =>
        document.Payload.ValueKind == JsonValueKind.Object
        && document.Payload.TryGetProperty("localPath", out _);

    private static ReaderDocumentRecord ToReaderDocument(DocumentRecord document)
    {
        var payload = document.Payload;
        var path = payload.TryGetProperty("localPath", out var pathValue) && pathValue.ValueKind == JsonValueKind.String
            ? pathValue.GetString() ?? string.Empty
            : string.Empty;
        var extensionData = payload.EnumerateObject()
            .Where(property => !OwnedPayloadFields.Contains(property.Name))
            .ToDictionary(
                property => property.Name,
                property => (object?)property.Value.Clone(),
                StringComparer.Ordinal);
        return new ReaderDocumentRecord
        {
            Id = document.Id,
            Name = document.Name,
            Path = path,
            Size = document.Size,
            LastModified = document.LastModified,
            Reader = ReadPayload<ReaderState>(payload, "reader") ?? new ReaderState(),
            Position = ReadPayload<ReaderPosition>(payload, "position") ?? new ReaderPosition(),
            ExtensionData = extensionData,
        };
    }

    private static T? ReadPayload<T>(JsonElement payload, string name)
        where T : class
    {
        if (!payload.TryGetProperty(name, out var value))
        {
            return null;
        }

        try
        {
            return value.Deserialize<T>(Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DocumentRecord ToNativeDocument(ReaderDocumentRecord document, DocumentRecord? previous)
    {
        var payload = previous?.Payload.ValueKind == JsonValueKind.Object
            ? previous.Payload.EnumerateObject().ToDictionary(
                property => property.Name,
                property => property.Value.Clone(),
                StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (name, value) in document.ExtensionData)
        {
            if (!OwnedPayloadFields.Contains(name))
            {
                payload[name] = JsonSerializer.SerializeToElement(value, Json);
            }
        }

        payload["reader"] = JsonSerializer.SerializeToElement(document.Reader, Json);
        payload["localPath"] = JsonSerializer.SerializeToElement(document.Path, Json);
        payload["position"] = JsonSerializer.SerializeToElement(document.Position, Json);
        return new DocumentRecord
        {
            Id = document.Id,
            Name = document.Name,
            Size = document.Size,
            LastModified = document.LastModified,
            Available = !string.IsNullOrWhiteSpace(document.Path),
            Payload = JsonSerializer.SerializeToElement(payload, Json),
            ExtensionData = previous?.ExtensionData ?? [],
        };
    }
}
