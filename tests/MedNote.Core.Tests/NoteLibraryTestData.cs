using System.Text.Json;
using MedNote.Core;
using MedNote.Core.Compatibility.WebV6;

namespace MedNote.Core.Tests;

internal static class NoteLibraryTestData
{
    public static NativeLibrarySnapshot Create(int sheetCount = 2)
    {
        if (sheetCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sheetCount));
        }

        var sheets = Enumerable.Range(1, sheetCount)
            .Select(index => new SheetRecord { Id = $"sheet-{index}", PageId = "page-1", Order = index - 1 })
            .ToList();
        var contents = sheets.ToDictionary(
            sheet => sheet.Id,
            sheet => new RtfSheetContent { Rtf = $@"{{\rtf1\ansi\pard Content for {sheet.Id}\par}}" },
            StringComparer.Ordinal);
        return new NativeLibrarySnapshot
        {
            Notes = new NoteStructure
            {
                Workspace = new WorkspaceRecord { Id = "workspace", Title = "MedNote" },
                Notebooks =
                [
                    new NotebookRecord { Id = "notebook-1", Title = "Nội tiết", Order = 0 },
                ],
                Sections =
                [
                    new SectionRecord { Id = "section-1", NotebookId = "notebook-1", Title = "Đái tháo đường", Order = 0 },
                    new SectionRecord { Id = "section-2", NotebookId = "notebook-1", Title = "Tuyến giáp", Order = 1 },
                ],
                Pages =
                [
                    new PageRecord { Id = "page-1", SectionId = "section-1", Title = "Điều trị", Order = 0 },
                ],
                Sheets = sheets,
                Active = new ActiveNoteState
                {
                    ActiveNotebookId = "notebook-1",
                    ActiveSectionId = "section-1",
                    ActivePageId = "page-1",
                    ActiveSheetId = "sheet-1",
                },
            },
            SheetContents = contents,
            Preferences = new LibraryPreferences
            {
                ActiveDocumentContextId = string.Empty,
                ReaderShare = 50,
                WorkspaceMode = WorkspaceMode.Note,
                NoteZoom = 1,
            },
            SavedAt = 1,
        };
    }

    public static NativeLibrarySnapshot WithLinkedDocument(NativeLibrarySnapshot source)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return source with
        {
            Documents = new DocumentGraph
            {
                Documents =
                [
                    new DocumentRecord
                    {
                        Id = "doc-1",
                        Name = "Harrison.pdf",
                        Size = 10,
                        LastModified = 20,
                        Available = true,
                        Payload = JsonSerializer.SerializeToElement(new { reader = new { page = 12 } }),
                    },
                ],
                Contexts =
                [
                    new DocumentContextRecord
                    {
                        Id = "context-1",
                        Kind = "document",
                        Name = "Harrison",
                        DocumentIds = ["doc-1"],
                        ActiveDocumentId = "doc-1",
                        SourcePage = 12,
                    },
                ],
                Links =
                [
                    new NoteDocumentLink
                    {
                        Id = "link-1",
                        DocumentId = "doc-1",
                        TargetType = DocumentLinkTargetType.Page,
                        TargetId = "page-1",
                    },
                ],
                LinkRelations =
                [
                    new DocumentLinkRelation
                    {
                        Id = "relation-1",
                        LinkIds = ["link-1"],
                        Kind = DocumentRelationKind.Workspace,
                        SourceType = DocumentRelationSourceType.Document,
                        SourceId = "doc-1",
                        CreatedAt = now,
                        UpdatedAt = now,
                    },
                ],
            },
            Preferences = source.Preferences with { ActiveDocumentContextId = "context-1" },
        };
    }

    public static WebLibraryV6 CreateWeb(int sheetCount = 2)
    {
        var native = Create(sheetCount);
        return new WebLibraryV6
        {
            Notes = native.Notes,
            SheetContents = native.Notes.Sheets.ToDictionary(
                sheet => sheet.Id,
                sheet => JsonSerializer.SerializeToElement(new Dictionary<string, object?>
                {
                    ["body"] = $"Content for {sheet.Id}",
                    ["futureSheetField"] = new { enabled = true },
                }),
                StringComparer.Ordinal),
            Documents = native.Documents,
            Preferences = native.Preferences,
            SavedAt = native.SavedAt,
        };
    }

    public static WebLibraryV6 CreateLinkedWeb(int sheetCount = 2)
    {
        var web = CreateWeb(sheetCount);
        var native = WithLinkedDocument(Create(sheetCount));
        return web with
        {
            Documents = native.Documents,
            Preferences = native.Preferences,
        };
    }
}

internal sealed class TemporaryRepositoryDirectory : IDisposable
{
    public TemporaryRepositoryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mednote-native-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
