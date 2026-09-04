using System.Text.Json;
using MedNote.Core;

namespace MedNote.Infrastructure;

/// <summary>
/// Presents the native Document graph through the existing Reader persistence
/// contract so Reader and Note no longer maintain two live libraries.
/// </summary>
public sealed class NativeReaderLibraryStore(INoteRepository repository) : IReaderLibraryStore
{
    private static readonly JsonSerializerOptions Json = JsonDefaults.Create();
    private static readonly HashSet<string> OwnedPayloadFields = new(StringComparer.Ordinal)
    {
        "reader",
        "localPath",
        "position",
    };

    private readonly INoteRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async ValueTask<ReaderLibrary> LoadAsync(CancellationToken cancellationToken = default)
    {
        var graph = await _repository.LoadDocumentGraphAsync(cancellationToken);
        if (graph is null)
        {
            return new ReaderLibrary();
        }

        var documents = graph.Documents
            .Where(HasReaderPayload)
            .Select(ToReaderDocument)
            .ToList();
        var documentIds = documents.Select(document => document.Id).ToHashSet(StringComparer.Ordinal);
        var context = graph.Contexts.FirstOrDefault(item => item.Id == ReaderV1Migration.ContextId);
        var activeDocumentId = context?.ActiveDocumentId is { } active && documentIds.Contains(active)
            ? active
            : documents.FirstOrDefault()?.Id;
        var runtime = await _repository.LoadRuntimeMetadataAsync(cancellationToken);
        return new ReaderLibrary
        {
            Documents = documents,
            ActiveDocumentId = activeDocumentId,
            SavedAt = runtime?.SavedAt ?? 0,
        };
    }

    public async ValueTask SaveAsync(ReaderLibrary library, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        var graph = await _repository.LoadDocumentGraphAsync(cancellationToken)
            ?? throw new InvalidDataException("Chưa khởi tạo Note Library native.");
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
        if (readerIds.Count > 0)
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

        await _repository.ReplaceDocumentGraphAsync(
            graph with { Documents = documents, Contexts = contexts },
            cancellationToken);
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
        var reader = ReadPayload<ReaderState>(payload, "reader") ?? new ReaderState();
        var position = ReadPayload<ReaderPosition>(payload, "position") ?? new ReaderPosition();
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
            Reader = reader,
            Position = position,
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
