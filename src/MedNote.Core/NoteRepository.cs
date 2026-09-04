namespace MedNote.Core;

public sealed record SaveDocumentWorkspaceRequest
{
    public List<DocumentRecord> Documents { get; init; } = [];

    public DocumentContextRecord Context { get; init; } = new();

    public DocumentGroupRecord? Group { get; init; }

    public List<NoteDocumentLink> Links { get; init; } = [];

    public List<DocumentLinkRelation> LinkRelations { get; init; } = [];
}

public interface INoteRepository : IAsyncDisposable
{
    ValueTask<NativeLibrarySnapshot?> LoadLibraryAsync(CancellationToken cancellationToken = default);

    ValueTask<LibraryRuntimeMetadata?> LoadRuntimeMetadataAsync(CancellationToken cancellationToken = default);

    ValueTask<NoteStructure?> LoadNoteStructureAsync(CancellationToken cancellationToken = default);

    ValueTask<DocumentGraph?> LoadDocumentGraphAsync(CancellationToken cancellationToken = default);

    ValueTask<HydratedSheet?> LoadSheetAsync(string sheetId, CancellationToken cancellationToken = default);

    ValueTask<RtfSheetContent?> LoadSheetContentAsync(string sheetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages, reloads and verifies a complete snapshot before atomically
    /// making it current. Failure must leave the previous snapshot current.
    /// </summary>
    ValueTask ReplaceLibraryAsync(NativeLibrarySnapshot library, CancellationToken cancellationToken = default);

    ValueTask<ActiveNoteState> CreateNotebookAsync(
        CreateNotebookRequest request,
        RtfSheetContent? initialContent = null,
        CancellationToken cancellationToken = default);

    ValueTask<string> CreateSectionAsync(CreateSectionRequest request, CancellationToken cancellationToken = default);

    ValueTask<ActiveNoteState> CreatePageAsync(
        CreatePageRequest request,
        RtfSheetContent? initialContent = null,
        CancellationToken cancellationToken = default);

    ValueTask<ActiveNoteState> CreateSheetAsync(
        CreateSheetRequest request,
        RtfSheetContent? initialContent = null,
        CancellationToken cancellationToken = default);

    ValueTask RenameNotebookAsync(string id, string title, CancellationToken cancellationToken = default);

    ValueTask RenameSectionAsync(string id, string title, CancellationToken cancellationToken = default);

    ValueTask RenamePageAsync(string id, string title, CancellationToken cancellationToken = default);

    ValueTask MovePageAsync(string id, string sectionId, int order, CancellationToken cancellationToken = default);

    ValueTask MoveSheetAsync(string id, string pageId, int order, CancellationToken cancellationToken = default);

    ValueTask DeleteNotebookAsync(string id, CancellationToken cancellationToken = default);

    ValueTask DeleteSectionAsync(string id, CancellationToken cancellationToken = default);

    ValueTask DeletePageAsync(string id, CancellationToken cancellationToken = default);

    ValueTask DeleteSheetAsync(string id, CancellationToken cancellationToken = default);

    ValueTask SaveSheetContentAsync(string sheetId, RtfSheetContent content, CancellationToken cancellationToken = default);

    ValueTask SetPreferencesAsync(LibraryPreferences preferences, CancellationToken cancellationToken = default);

    ValueTask SetActiveStateAsync(ActiveNoteState active, CancellationToken cancellationToken = default);

    ValueTask ReplaceDocumentGraphAsync(DocumentGraph graph, CancellationToken cancellationToken = default);

    ValueTask<DocumentGraph> SaveDocumentWorkspaceAsync(SaveDocumentWorkspaceRequest request, CancellationToken cancellationToken = default);

    ValueTask<DocumentGraph> DeleteDocumentWorkspaceAsync(string contextId, CancellationToken cancellationToken = default);

    ValueTask<DocumentGraph> DeleteDocumentFromWorkspaceAsync(
        string contextId,
        string documentId,
        CancellationToken cancellationToken = default);

    ValueTask UpsertDocumentAsync(DocumentRecord document, CancellationToken cancellationToken = default);

    ValueTask UpsertDocumentContextAsync(DocumentContextRecord context, CancellationToken cancellationToken = default);

    ValueTask UpsertDocumentGroupAsync(DocumentGroupRecord group, CancellationToken cancellationToken = default);

    ValueTask UpsertDocumentLinkAsync(NoteDocumentLink link, CancellationToken cancellationToken = default);

    ValueTask UpsertDocumentLinkRelationAsync(DocumentLinkRelation relation, CancellationToken cancellationToken = default);

    ValueTask DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default);

    ValueTask DeleteDocumentLinkAsync(string linkId, CancellationToken cancellationToken = default);
}
