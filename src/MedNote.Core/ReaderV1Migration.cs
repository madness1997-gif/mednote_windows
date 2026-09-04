using System.Text.Json;

namespace MedNote.Core;

/// <summary>
/// One-way bridge used at the M4 cut-over. It does not delete or rewrite the
/// Reader v1 file; the caller may keep it until the native repository has been
/// staged, reloaded and verified.
/// </summary>
public static class ReaderV1Migration
{
    public const string ContextId = "reader-library-v1";

    public static NativeLibrarySnapshot CreateLibrary(ReaderLibrary readerLibrary)
    {
        ArgumentNullException.ThrowIfNull(readerLibrary);
        var documents = readerLibrary.Documents.Select(ToDocument).ToList();
        var documentIds = documents.Select(document => document.Id).ToList();
        var activeDocumentId = readerLibrary.ActiveDocumentId is not null
            && documentIds.Contains(readerLibrary.ActiveDocumentId, StringComparer.Ordinal)
                ? readerLibrary.ActiveDocumentId
                : documentIds.FirstOrDefault();
        List<DocumentContextRecord> context = documents.Count == 0
            ? []
            : new List<DocumentContextRecord>
            {
                new()
                {
                    Id = ContextId,
                    Kind = documents.Count > 1 ? "collection" : "document",
                    Name = "Reader",
                    DocumentIds = documentIds,
                    ActiveDocumentId = activeDocumentId,
                    SourcePage = documents.FirstOrDefault(document => document.Id == activeDocumentId) is { } active
                        ? ReaderPage(active)
                        : 1,
                },
            };
        var library = new NativeLibrarySnapshot
        {
            Notes = new NoteStructure
            {
                Workspace = new WorkspaceRecord { Id = "workspace", Title = "MedNote" },
            },
            Documents = new DocumentGraph
            {
                Documents = documents,
                Contexts = context,
            },
            Preferences = new LibraryPreferences
            {
                ActiveDocumentContextId = context.Count == 0 ? string.Empty : ContextId,
                WorkspaceMode = context.Count == 0 ? WorkspaceMode.Note : WorkspaceMode.Reader,
            },
            SavedAt = readerLibrary.SavedAt,
            ExtensionData = readerLibrary.ExtensionData.Count == 0
                ? []
                : new Dictionary<string, JsonElement>
                {
                    ["readerV1Extension"] = JsonSerializer.SerializeToElement(readerLibrary.ExtensionData, JsonDefaults.Create()),
                },
        };
        NoteLibraryValidator.AssertValid(library);
        return library;
    }

    private static DocumentRecord ToDocument(ReaderDocumentRecord source)
    {
        var payload = source.ExtensionData.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        payload["reader"] = source.Reader;
        payload["localPath"] = source.Path;
        payload["position"] = source.Position;
        return new DocumentRecord
        {
            Id = source.Id,
            Name = source.Name,
            Size = source.Size,
            LastModified = source.LastModified,
            Available = !string.IsNullOrWhiteSpace(source.Path),
            Payload = JsonSerializer.SerializeToElement(payload, JsonDefaults.Create()),
        };
    }

    private static int ReaderPage(DocumentRecord document) =>
        document.Payload.TryGetProperty("reader", out var reader)
        && reader.TryGetProperty("page", out var page)
        && page.TryGetInt32(out var value)
            ? Math.Max(1, value)
            : 1;
}
