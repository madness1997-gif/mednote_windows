using System.Text.Json;
using MedNote.Core;

namespace MedNote.Infrastructure;

/// <summary>
/// Native record store with an atomically replaced metadata manifest and
/// immutable, content-addressed RTF Sheet blobs. Metadata-only startup never
/// opens Sheet content files; a failed staged replacement leaves the current
/// manifest untouched.
/// </summary>
public sealed partial class FileNoteRepository : INoteRepository
{
    public const string ManifestFileName = "manifest-v1.json";

    public const string BlobDirectoryName = "sheet-blobs";

    private const int StoreFormatVersion = 1;
    private readonly string _rootPath;
    private readonly string _manifestPath;
    private readonly string _blobPath;
    private readonly JsonSerializerOptions _compactJson = JsonDefaults.Create();
    private readonly JsonSerializerOptions _manifestJson = JsonDefaults.Create(writeIndented: true);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposed;

    public FileNoteRepository(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = Path.GetFullPath(rootPath);
        _manifestPath = Path.Combine(_rootPath, ManifestFileName);
        _blobPath = Path.Combine(_rootPath, BlobDirectoryName);
    }

    public async ValueTask<NativeLibrarySnapshot?> LoadLibraryAsync(CancellationToken cancellationToken = default) =>
        await LockedAsync(async () =>
        {
            var manifest = await ReadManifestAsync(required: false, cancellationToken);
            return manifest is null ? null : await LoadLibraryAsync(manifest, cancellationToken);
        }, cancellationToken);

    public async ValueTask<LibraryRuntimeMetadata?> LoadRuntimeMetadataAsync(CancellationToken cancellationToken = default) =>
        await LockedAsync(async () =>
        {
            var manifest = await ReadManifestAsync(required: false, cancellationToken);
            return manifest is null
                ? null
                : new LibraryRuntimeMetadata
                {
                    Preferences = Clone(manifest.Preferences),
                    SavedAt = manifest.SavedAt,
                };
        }, cancellationToken);

    public async ValueTask<NoteStructure?> LoadNoteStructureAsync(CancellationToken cancellationToken = default) =>
        await LockedAsync(async () =>
        {
            var manifest = await ReadManifestAsync(required: false, cancellationToken);
            return manifest is null ? null : Clone(manifest.Notes);
        }, cancellationToken);

    public async ValueTask<DocumentGraph?> LoadDocumentGraphAsync(CancellationToken cancellationToken = default) =>
        await LockedAsync(async () =>
        {
            var manifest = await ReadManifestAsync(required: false, cancellationToken);
            return manifest is null ? null : Clone(manifest.Documents);
        }, cancellationToken);

    public async ValueTask<HydratedSheet?> LoadSheetAsync(string sheetId, CancellationToken cancellationToken = default) =>
        await LockedAsync(async () =>
        {
            var manifest = await ReadManifestAsync(required: false, cancellationToken);
            if (manifest is null)
            {
                return null;
            }

            var sheet = manifest.Notes.Sheets.FirstOrDefault(record => record.Id == sheetId);
            return sheet is null
                ? null
                : new HydratedSheet
                {
                    Sheet = Clone(sheet),
                    Content = await ReadSheetContentAsync(sheetId, manifest.SheetBlobs[sheetId], cancellationToken),
                };
        }, cancellationToken);

    public async ValueTask<RtfSheetContent?> LoadSheetContentAsync(string sheetId, CancellationToken cancellationToken = default) =>
        await LockedAsync(async () =>
        {
            var manifest = await ReadManifestAsync(required: false, cancellationToken);
            if (manifest is null || !manifest.SheetBlobs.TryGetValue(sheetId, out var reference))
            {
                return null;
            }

            return await ReadSheetContentAsync(sheetId, reference, cancellationToken);
        }, cancellationToken);

    public async ValueTask ReplaceLibraryAsync(NativeLibrarySnapshot library, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        await LockedAsync(async () =>
        {
            await ReplaceLibraryCoreAsync(Clone(library), cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async ValueTask<ActiveNoteState> CreateNotebookAsync(
        CreateNotebookRequest request,
        RtfSheetContent? initialContent = null,
        CancellationToken cancellationToken = default) =>
        await LockedAsync(async () =>
        {
            var manifest = await RequireManifestAsync(cancellationToken);
            var mutation = NoteHierarchyOperations.CreateNotebook(manifest.Notes, request);
            manifest = await ApplyHierarchyMutationAsync(manifest, mutation, initialContent, cancellationToken);
            await CommitManifestAsync(manifest, cancellationToken);
            return Clone(manifest.Notes.Active);
        }, cancellationToken);

    public async ValueTask<string> CreateSectionAsync(CreateSectionRequest request, CancellationToken cancellationToken = default) =>
        await LockedAsync(async () =>
        {
            var manifest = await RequireManifestAsync(cancellationToken);
            var previous = manifest.Notes.Sections.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
            var mutation = NoteHierarchyOperations.CreateSection(manifest.Notes, request);
            var created = mutation.Notes.Sections.Single(record => !previous.Contains(record.Id));
            manifest = Touch(manifest with { Notes = mutation.Notes });
            await CommitManifestAsync(manifest, cancellationToken);
            return created.Id;
        }, cancellationToken);

    public async ValueTask<ActiveNoteState> CreatePageAsync(
        CreatePageRequest request,
        RtfSheetContent? initialContent = null,
        CancellationToken cancellationToken = default) =>
        await LockedAsync(async () =>
        {
            var manifest = await RequireManifestAsync(cancellationToken);
            var mutation = NoteHierarchyOperations.CreatePage(manifest.Notes, request);
            manifest = await ApplyHierarchyMutationAsync(manifest, mutation, initialContent, cancellationToken);
            await CommitManifestAsync(manifest, cancellationToken);
            return Clone(manifest.Notes.Active);
        }, cancellationToken);

    public async ValueTask<ActiveNoteState> CreateSheetAsync(
        CreateSheetRequest request,
        RtfSheetContent? initialContent = null,
        CancellationToken cancellationToken = default) =>
        await LockedAsync(async () =>
        {
            var manifest = await RequireManifestAsync(cancellationToken);
            var mutation = NoteHierarchyOperations.CreateSheet(manifest.Notes, request);
            manifest = await ApplyHierarchyMutationAsync(manifest, mutation, initialContent, cancellationToken);
            await CommitManifestAsync(manifest, cancellationToken);
            return Clone(manifest.Notes.Active);
        }, cancellationToken);

    public ValueTask RenameNotebookAsync(string id, string title, CancellationToken cancellationToken = default) =>
        UpdateNotesAsync(notes => NoteHierarchyOperations.RenameNotebook(notes, id, title), cancellationToken);

    public ValueTask RenameSectionAsync(string id, string title, CancellationToken cancellationToken = default) =>
        UpdateNotesAsync(notes => NoteHierarchyOperations.RenameSection(notes, id, title), cancellationToken);

    public ValueTask RenamePageAsync(string id, string title, CancellationToken cancellationToken = default) =>
        UpdateNotesAsync(notes => NoteHierarchyOperations.RenamePage(notes, id, title), cancellationToken);

    public ValueTask MovePageAsync(string id, string sectionId, int order, CancellationToken cancellationToken = default) =>
        UpdateNotesAsync(notes => NoteHierarchyOperations.MovePage(notes, id, sectionId, order), cancellationToken);

    public ValueTask MoveSheetAsync(string id, string pageId, int order, CancellationToken cancellationToken = default) =>
        UpdateNotesAsync(notes => NoteHierarchyOperations.MoveSheet(notes, id, pageId, order), cancellationToken);

    public ValueTask DeleteNotebookAsync(string id, CancellationToken cancellationToken = default) =>
        DeleteHierarchyAsync(notes => NoteHierarchyOperations.DeleteNotebook(notes, id), cancellationToken);

    public ValueTask DeleteSectionAsync(string id, CancellationToken cancellationToken = default) =>
        DeleteHierarchyAsync(notes => NoteHierarchyOperations.DeleteSection(notes, id), cancellationToken);

    public ValueTask DeletePageAsync(string id, CancellationToken cancellationToken = default) =>
        DeleteHierarchyAsync(notes => NoteHierarchyOperations.DeletePage(notes, id), cancellationToken);

    public ValueTask DeleteSheetAsync(string id, CancellationToken cancellationToken = default) =>
        DeleteHierarchyAsync(notes => NoteHierarchyOperations.DeleteSheet(notes, id), cancellationToken);

    public async ValueTask SaveSheetContentAsync(string sheetId, RtfSheetContent content, CancellationToken cancellationToken = default)
    {
        await LockedAsync(async () =>
        {
            var manifest = await RequireManifestAsync(cancellationToken);
            if (!manifest.Notes.Sheets.Any(record => record.Id == sheetId))
            {
                throw new NoteRepositoryMutationException($"Không tìm thấy Sheet {sheetId}.");
            }

            NoteLibraryValidator.AssertSheetContentValid(sheetId, content);
            var reference = await WriteSheetBlobAsync(content, cancellationToken);
            var blobs = new Dictionary<string, SheetBlobReference>(manifest.SheetBlobs, StringComparer.Ordinal)
            {
                [sheetId] = reference,
            };
            await CommitManifestAsync(Touch(manifest with { SheetBlobs = blobs }), cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async ValueTask<DocumentGraph> SaveLinkedSheetContentAsync(
        string sheetId,
        RtfSheetContent content,
        NoteDocumentLink link,
        DocumentLinkRelation relation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(relation);
        return await LockedAsync(async () =>
        {
            var manifest = await RequireManifestAsync(cancellationToken);
            if (!manifest.Notes.Sheets.Any(record => record.Id == sheetId))
            {
                throw new NoteRepositoryMutationException($"Không tìm thấy Sheet {sheetId}.");
            }

            if (link.TargetType != DocumentLinkTargetType.Sheet || link.TargetId != sheetId)
            {
                throw new NoteRepositoryMutationException("Liên kết PDF không trỏ tới Sheet đang lưu.");
            }

            NoteLibraryValidator.AssertSheetContentValid(sheetId, content);
            var documents = DocumentGraphOperations.UpsertContentLink(
                manifest.Documents,
                Clone(link),
                Clone(relation));
            NoteLibraryValidator.AssertDocumentGraphValid(documents, manifest.Notes);
            var reference = await WriteSheetBlobAsync(content, cancellationToken);
            var blobs = new Dictionary<string, SheetBlobReference>(manifest.SheetBlobs, StringComparer.Ordinal)
            {
                [sheetId] = reference,
            };
            await CommitManifestAsync(
                Touch(manifest with { Documents = documents, SheetBlobs = blobs }),
                cancellationToken);
            return Clone(documents);
        }, cancellationToken);
    }

    public async ValueTask SetPreferencesAsync(LibraryPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        await LockedAsync(async () =>
        {
            var manifest = await RequireManifestAsync(cancellationToken);
            manifest = Touch(manifest with { Preferences = Clone(preferences) });
            ValidateManifest(manifest, requireBlobFiles: true);
            await CommitManifestAsync(manifest, cancellationToken);
            return true;
        }, cancellationToken);
    }

    public ValueTask SetActiveStateAsync(ActiveNoteState active, CancellationToken cancellationToken = default) =>
        UpdateNotesAsync(notes => NoteHierarchyOperations.SetActive(notes, active), cancellationToken);

    public async ValueTask ReplaceDocumentGraphAsync(DocumentGraph graph, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        await UpdateDocumentsAsync(_ => Clone(graph), cancellationToken);
    }

    public async ValueTask<DocumentGraph> SaveDocumentWorkspaceAsync(
        SaveDocumentWorkspaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await UpdateDocumentsAndReturnAsync(
            graph => DocumentGraphOperations.SaveWorkspace(graph, Clone(request)),
            cancellationToken);
    }

    public async ValueTask<DocumentGraph> DeleteDocumentWorkspaceAsync(
        string contextId,
        CancellationToken cancellationToken = default) =>
        await UpdateDocumentsAndReturnAsync(
            graph => DocumentGraphOperations.DeleteWorkspace(graph, contextId),
            cancellationToken);

    public async ValueTask<DocumentGraph> DeleteDocumentFromWorkspaceAsync(
        string contextId,
        string documentId,
        CancellationToken cancellationToken = default) =>
        await UpdateDocumentsAndReturnAsync(
            graph => DocumentGraphOperations.DeleteDocumentFromWorkspace(graph, contextId, documentId),
            cancellationToken);

    public ValueTask UpsertDocumentAsync(DocumentRecord document, CancellationToken cancellationToken = default) =>
        UpdateDocumentsAsync(graph => DocumentGraphOperations.UpsertDocument(graph, Clone(document)), cancellationToken);

    public ValueTask UpsertDocumentContextAsync(DocumentContextRecord context, CancellationToken cancellationToken = default) =>
        UpdateDocumentsAsync(graph => DocumentGraphOperations.UpsertContext(graph, Clone(context)), cancellationToken);

    public ValueTask UpsertDocumentGroupAsync(DocumentGroupRecord group, CancellationToken cancellationToken = default) =>
        UpdateDocumentsAsync(graph => DocumentGraphOperations.UpsertGroup(graph, Clone(group)), cancellationToken);

    public ValueTask UpsertDocumentLinkAsync(NoteDocumentLink link, CancellationToken cancellationToken = default) =>
        UpdateDocumentsAsync(graph => DocumentGraphOperations.UpsertLink(graph, Clone(link)), cancellationToken);

    public ValueTask UpsertDocumentLinkRelationAsync(DocumentLinkRelation relation, CancellationToken cancellationToken = default) =>
        UpdateDocumentsAsync(graph => DocumentGraphOperations.UpsertLinkRelation(graph, Clone(relation)), cancellationToken);

    public ValueTask DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default) =>
        UpdateDocumentsAsync(graph => DocumentGraphOperations.DeleteDocument(graph, documentId), cancellationToken);

    public ValueTask DeleteDocumentLinkAsync(string linkId, CancellationToken cancellationToken = default) =>
        UpdateDocumentsAsync(graph => DocumentGraphOperations.RemoveLink(graph, linkId), cancellationToken);

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _gate.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private async ValueTask UpdateNotesAsync(Func<NoteStructure, NoteStructure> update, CancellationToken cancellationToken)
    {
        await LockedAsync(async () =>
        {
            var manifest = await RequireManifestAsync(cancellationToken);
            manifest = Touch(manifest with { Notes = update(manifest.Notes) });
            await CommitManifestAsync(manifest, cancellationToken);
            return true;
        }, cancellationToken);
    }

    private async ValueTask DeleteHierarchyAsync(Func<NoteStructure, HierarchyMutation> update, CancellationToken cancellationToken)
    {
        await LockedAsync(async () =>
        {
            var manifest = await RequireManifestAsync(cancellationToken);
            var previousPages = manifest.Notes.Pages.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
            var previousSheets = manifest.Notes.Sheets.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
            var mutation = update(manifest.Notes);
            var remainingPages = mutation.Notes.Pages.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
            var remainingSheets = mutation.Notes.Sheets.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
            var removedPages = previousPages.Except(remainingPages, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
            var removedSheets = previousSheets.Except(remainingSheets, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
            var blobs = manifest.SheetBlobs
                .Where(item => !removedSheets.Contains(item.Key))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            var documents = DocumentGraphOperations.RemoveTargets(manifest.Documents, removedPages, removedSheets);
            manifest = Touch(manifest with
            {
                Notes = mutation.Notes,
                Documents = documents,
                SheetBlobs = blobs,
            });
            await CommitManifestAsync(manifest, cancellationToken);
            return true;
        }, cancellationToken);
    }

    private async ValueTask UpdateDocumentsAsync(Func<DocumentGraph, DocumentGraph> update, CancellationToken cancellationToken)
    {
        _ = await UpdateDocumentsAndReturnAsync(update, cancellationToken);
    }

    private async ValueTask<DocumentGraph> UpdateDocumentsAndReturnAsync(
        Func<DocumentGraph, DocumentGraph> update,
        CancellationToken cancellationToken)
    {
        return await LockedAsync(async () =>
        {
            var manifest = await RequireManifestAsync(cancellationToken);
            var documents = update(manifest.Documents);
            NoteLibraryValidator.AssertDocumentGraphValid(documents, manifest.Notes);
            await CommitManifestAsync(Touch(manifest with { Documents = documents }), cancellationToken);
            return Clone(documents);
        }, cancellationToken);
    }

    private async Task<FileLibraryManifest> ApplyHierarchyMutationAsync(
        FileLibraryManifest manifest,
        HierarchyMutation mutation,
        RtfSheetContent? initialContent,
        CancellationToken cancellationToken)
    {
        var blobs = new Dictionary<string, SheetBlobReference>(manifest.SheetBlobs, StringComparer.Ordinal);
        foreach (var id in mutation.RemovedSheetIds)
        {
            blobs.Remove(id);
        }

        var content = initialContent ?? RtfSheetContent.CreateEmpty();
        foreach (var id in mutation.CreatedSheetIds)
        {
            NoteLibraryValidator.AssertSheetContentValid(id, content);
            blobs[id] = await WriteSheetBlobAsync(content, cancellationToken);
        }

        return Touch(manifest with { Notes = mutation.Notes, SheetBlobs = blobs });
    }
}
