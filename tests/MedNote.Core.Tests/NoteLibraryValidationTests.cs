using System.Text.Json;
using MedNote.Core;
using MedNote.Core.Compatibility.WebV6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class NoteLibraryValidationTests
{
    [TestMethod]
    public void Validate_RejectsMissingOrOrphanSheetContent()
    {
        var library = NoteLibraryTestData.Create();
        library.SheetContents.Remove("sheet-1");
        library.SheetContents["orphan"] = RtfSheetContent.CreateEmpty();

        var issues = NoteLibraryValidator.Validate(library);

        Assert.IsTrue(issues.Any(issue => issue.Code == "missing-content" && issue.Id == "sheet-1"));
        Assert.IsTrue(issues.Any(issue => issue.Code == "orphan-content" && issue.Id == "orphan"));
    }

    [TestMethod]
    public void WebV6Validate_RejectsNavigationMetadataInsideSheetContent()
    {
        var library = NoteLibraryTestData.CreateWeb(1);
        library.SheetContents["sheet-1"] = JsonSerializer.SerializeToElement(new
        {
            body = "Metformin",
            pageId = "page-1",
        });

        var exception = Assert.ThrowsExactly<NoteLibraryValidationException>(() => WebV6LibraryValidator.AssertValid(library));

        Assert.IsTrue(exception.Issues.Any(issue => issue.Code == "navigation-metadata-in-content"));
    }

    [TestMethod]
    public void NativeValidate_RejectsNonRtfSheetContent()
    {
        var library = NoteLibraryTestData.Create(1);
        library.SheetContents["sheet-1"] = new RtfSheetContent { Rtf = "plain text" };

        var exception = Assert.ThrowsExactly<NoteLibraryValidationException>(() => NoteLibraryValidator.AssertValid(library));

        Assert.IsTrue(exception.Issues.Any(issue => issue.Code == "invalid-content"));
    }

    [TestMethod]
    public void Validate_RejectsBrokenHierarchyAndLinks()
    {
        var source = NoteLibraryTestData.WithLinkedDocument(NoteLibraryTestData.Create(1));
        var library = source with
        {
            Notes = source.Notes with
            {
                Sections = [source.Notes.Sections[0] with { NotebookId = "missing" }, source.Notes.Sections[1]],
            },
            Documents = source.Documents with
            {
                Links = [source.Documents.Links[0] with { TargetId = "missing-page" }],
            },
        };

        var issues = NoteLibraryValidator.Validate(library);

        Assert.IsTrue(issues.Any(issue => issue.Code == "missing-parent"));
        Assert.IsTrue(issues.Any(issue => issue.Code == "missing-target"));
        Assert.IsTrue(issues.Any(issue => issue.Code == "invalid-active-chain"));
    }

    [TestMethod]
    public void WebJson_RoundTripPreservesUnknownFieldsAndCamelCaseEnums()
    {
        const string json = """
            {
              "version": 6,
              "notes": {
                "workspace": { "id": "workspace", "title": "MedNote", "futureWorkspace": 7 },
                "notebooks": [{ "id": "notebook", "title": "Nội tiết", "order": 0, "futureNotebook": true }],
                "sections": [{ "id": "section", "notebookId": "notebook", "title": "Tuyến giáp", "order": 0 }],
                "pages": [{ "id": "page", "sectionId": "section", "title": "Cường giáp", "order": 0 }],
                "sheets": [{ "id": "sheet", "pageId": "page", "order": 0 }],
                "active": { "activeNotebookId": "notebook", "activeSectionId": "section", "activePageId": "page", "activeSheetId": "sheet" }
              },
              "sheetContents": { "sheet": { "body": "Thiamazole", "futureContent": { "value": 9 } } },
              "documents": {
                "documents": [{ "id": "doc", "name": "Book.pdf", "size": 1, "lastModified": 2, "available": true, "payload": { "reader": { "page": 3 }, "futurePayload": "kept" } }],
                "contexts": [{ "id": "context", "kind": "document", "name": "Book", "documentIds": ["doc"], "activeDocumentId": "doc", "sourcePage": 3 }],
                "groups": [],
                "links": [{ "id": "link", "documentId": "doc", "targetType": "sheet", "targetId": "sheet" }],
                "linkRelations": [{ "id": "relation", "linkIds": ["link"], "kind": "workspace", "sourceType": "document", "sourceId": "doc", "createdAt": 1, "updatedAt": 1, "futureRelation": "kept" }]
              },
              "preferences": { "activeDocumentContextId": "context", "readerShare": 50, "workspaceMode": "split", "futurePreference": 11 },
              "savedAt": 5,
              "futureLibrary": { "enabled": true }
            }
            """;

        var library = JsonSerializer.Deserialize<WebLibraryV6>(json, JsonDefaults.Create());
        Assert.IsNotNull(library);
        WebV6LibraryValidator.AssertValid(library);
        var roundTrip = JsonSerializer.Serialize(library, JsonDefaults.Create());

        StringAssert.Contains(roundTrip, "\"targetType\":\"sheet\"");
        StringAssert.Contains(roundTrip, "\"workspaceMode\":\"split\"");
        StringAssert.Contains(roundTrip, "\"futureLibrary\"");
        StringAssert.Contains(roundTrip, "\"futureNotebook\":true");
        StringAssert.Contains(roundTrip, "\"futureRelation\":\"kept\"");
        StringAssert.Contains(roundTrip, "\"futureContent\"");
    }

    [TestMethod]
    public void WebJson_PreservesRequiredNullAndOmitsOptionalNulls()
    {
        var contextJson = JsonSerializer.Serialize(
            new DocumentContextRecord
            {
                Id = "context",
                Kind = "document",
                Name = "Empty",
                DocumentIds = [],
                ActiveDocumentId = null,
                SourcePage = 1,
            },
            JsonDefaults.Create());
        var preferencesJson = JsonSerializer.Serialize(
            new LibraryPreferences { ActiveDocumentContextId = string.Empty, ReaderShare = 50 },
            JsonDefaults.Create());

        StringAssert.Contains(contextJson, "\"activeDocumentId\":null");
        Assert.IsFalse(preferencesJson.Contains("workspaceMode", StringComparison.Ordinal));
        Assert.IsFalse(preferencesJson.Contains("noteZoom", StringComparison.Ordinal));
    }

    [TestMethod]
    public void WebJson_RejectsMissingRequiredField()
    {
        const string json = """
            { "id": "context", "kind": "document", "name": "Empty", "documentIds": [], "sourcePage": 1 }
            """;

        Assert.ThrowsExactly<JsonException>(() =>
            JsonSerializer.Deserialize<DocumentContextRecord>(json, JsonDefaults.Create()));
    }

    [TestMethod]
    public void Validate_ReportsExplicitNullCollectionsInsteadOfCrashing()
    {
        var source = NoteLibraryTestData.Create(1);
        var library = source with { Notes = source.Notes with { Sheets = null! } };

        var issues = NoteLibraryValidator.Validate(library);

        Assert.IsTrue(issues.Any(issue => issue.Code == "missing-note-root"));
    }

    [TestMethod]
    public void ReaderV1Migration_PreservesReaderStatePathPositionAndUnknownFields()
    {
        var reader = new ReaderLibrary
        {
            Documents =
            [
                new ReaderDocumentRecord
                {
                    Id = "doc-1",
                    Name = "Harrison.pdf",
                    Path = @"C:\Books\Harrison.pdf",
                    Size = 10,
                    LastModified = 20,
                    Reader = new ReaderState { Page = 17, Bookmarks = [2, 17] },
                    Position = new ReaderPosition { AnchorPage = 17, PageOffsetRatio = 0.42 },
                    ExtensionData = new Dictionary<string, object?> { ["futureReaderField"] = "kept" },
                },
            ],
            ActiveDocumentId = "doc-1",
            SavedAt = 99,
        };

        var library = ReaderV1Migration.CreateLibrary(reader);
        var payload = library.Documents.Documents.Single().Payload;

        Assert.AreEqual(17, payload.GetProperty("reader").GetProperty("page").GetInt32());
        Assert.AreEqual(17, payload.GetProperty("position").GetProperty("anchorPage").GetInt32());
        Assert.AreEqual(@"C:\Books\Harrison.pdf", payload.GetProperty("localPath").GetString());
        Assert.AreEqual("kept", payload.GetProperty("futureReaderField").GetString());
        Assert.AreEqual(17, library.Documents.Contexts.Single().SourcePage);
        NoteLibraryValidator.AssertValid(library);
    }
}
