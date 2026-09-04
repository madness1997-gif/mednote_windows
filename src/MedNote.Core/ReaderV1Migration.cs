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
        var graph = ReaderDocumentGraphBridge.Merge(new DocumentGraph(), readerLibrary);
        var library = new NativeLibrarySnapshot
        {
            Notes = new NoteStructure
            {
                Workspace = new WorkspaceRecord { Id = "workspace", Title = "MedNote" },
            },
            Documents = graph,
            Preferences = new LibraryPreferences
            {
                ActiveDocumentContextId = graph.Contexts.Count == 0 ? string.Empty : ContextId,
                WorkspaceMode = graph.Contexts.Count == 0 ? WorkspaceMode.Note : WorkspaceMode.Reader,
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
}
