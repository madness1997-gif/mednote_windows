using MedNote.Core;
using MedNote.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class FileNoteRepositoryTests
{
    [TestMethod]
    public async Task ReplaceLibrary_ReloadsExactNativeSnapshot()
    {
        using var directory = new TemporaryRepositoryDirectory();
        var expected = NoteLibraryTestData.WithLinkedDocument(NoteLibraryTestData.Create());
        expected = expected with
        {
            SheetContents = expected.SheetContents
                .Reverse()
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
        };
        await using (var repository = new FileNoteRepository(directory.Path))
        {
            await repository.ReplaceLibraryAsync(expected);
        }

        await using var reopened = new FileNoteRepository(directory.Path);
        var actual = await reopened.LoadLibraryAsync();

        Assert.IsNotNull(actual);
        NativeLibrarySnapshotVerifier.AssertEquivalent(expected, actual);
        Assert.IsTrue(File.Exists(System.IO.Path.Combine(directory.Path, FileNoteRepository.ManifestFileName)));
    }

    [TestMethod]
    public async Task StructureLoad_DoesNotOpenAnyOfThe150SheetBlobs()
    {
        using var directory = new TemporaryRepositoryDirectory();
        await using var repository = new FileNoteRepository(directory.Path);
        await repository.ReplaceLibraryAsync(NoteLibraryTestData.Create(150));
        var blobDirectory = System.IO.Path.Combine(directory.Path, FileNoteRepository.BlobDirectoryName);
        var blobs = Directory.GetFiles(blobDirectory, "*.rtf");
        Assert.AreEqual(150, blobs.Length);
        Assert.IsTrue(RtfDocument.IsRtf(await File.ReadAllTextAsync(blobs[0])));
        var firstContent = await repository.LoadSheetContentAsync("sheet-1");
        File.Delete(blobs[^1]);

        var structure = await repository.LoadNoteStructureAsync();

        Assert.IsNotNull(structure);
        Assert.IsNotNull(firstContent);
        Assert.AreEqual(150, structure.Sheets.Count);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
        {
            _ = await repository.LoadLibraryAsync();
        });
    }

    [TestMethod]
    public async Task MetadataMutation_ReusesImmutableSheetBlobs()
    {
        using var directory = new TemporaryRepositoryDirectory();
        await using var repository = new FileNoteRepository(directory.Path);
        await repository.ReplaceLibraryAsync(NoteLibraryTestData.Create(32));
        var blobDirectory = System.IO.Path.Combine(directory.Path, FileNoteRepository.BlobDirectoryName);
        var before = Directory.GetFiles(blobDirectory, "*.rtf").Select(System.IO.Path.GetFileName).Order().ToArray();

        await repository.RenamePageAsync("page-1", "Điều trị cập nhật");
        await repository.SetPreferencesAsync(new LibraryPreferences
        {
            ReaderShare = 44,
            WorkspaceMode = WorkspaceMode.Split,
            NoteZoom = 1.1,
        });
        var after = Directory.GetFiles(blobDirectory, "*.rtf").Select(System.IO.Path.GetFileName).Order().ToArray();

        CollectionAssert.AreEqual(before, after);
        Assert.AreEqual("Điều trị cập nhật", (await repository.LoadNoteStructureAsync())?.Pages.Single().Title);
        Assert.AreEqual(44d, (await repository.LoadRuntimeMetadataAsync())?.Preferences.ReaderShare);
    }

    [TestMethod]
    public async Task SaveSheetContent_RepairsAReusedCorruptBlobBeforeCommit()
    {
        using var directory = new TemporaryRepositoryDirectory();
        await using var repository = new FileNoteRepository(directory.Path);
        var library = NoteLibraryTestData.Create(1);
        await repository.ReplaceLibraryAsync(library);
        var blobDirectory = System.IO.Path.Combine(directory.Path, FileNoteRepository.BlobDirectoryName);
        var blobPath = Directory.GetFiles(blobDirectory, "*.rtf").Single();
        await File.WriteAllTextAsync(blobPath, "corrupt");

        await repository.SaveSheetContentAsync("sheet-1", library.SheetContents["sheet-1"]);
        var reloaded = await repository.LoadLibraryAsync();

        Assert.IsNotNull(reloaded);
        Assert.IsTrue(reloaded.SavedAt >= library.SavedAt);
        NativeLibrarySnapshotVerifier.AssertEquivalent(library with { SavedAt = reloaded.SavedAt }, reloaded);
    }

    [TestMethod]
    public async Task SaveLinkedSheetContent_CommitsRtfAndPdfAnchorTogether()
    {
        using var directory = new TemporaryRepositoryDirectory();
        await using var repository = new FileNoteRepository(directory.Path);
        var initial = NoteLibraryTestData.WithLinkedDocument(NoteLibraryTestData.Create(1));
        await repository.ReplaceLibraryAsync(initial);
        var content = new RtfSheetContent { Rtf = @"{\rtf1\ansi\pard Cropped source\par}" };
        var pair = PdfContentLinks.Create(
            "doc-1",
            "sheet-1",
            12,
            new PdfAnnotationRect(10, 20, 110, 220),
            createdAt: 123,
            linkId: "crop-link",
            relationId: "crop-relation");

        var graph = await repository.SaveLinkedSheetContentAsync(
            "sheet-1",
            content,
            pair.Link,
            pair.Relation);
        var reloaded = await repository.LoadLibraryAsync();

        Assert.IsNotNull(reloaded);
        Assert.AreEqual(content.Rtf, reloaded.SheetContents["sheet-1"].Rtf);
        Assert.IsTrue(graph.Links.Any(link => link.Id == "crop-link"));
        Assert.AreEqual(12, graph.LinkRelations.Single(relation => relation.Id == "crop-relation").ContentAnchor?.PdfPage);
        NoteLibraryValidator.AssertValid(reloaded);
    }

    [TestMethod]
    public async Task SaveLinkedSheetContent_RejectsMismatchedAnchorWithoutChangingRtf()
    {
        using var directory = new TemporaryRepositoryDirectory();
        await using var repository = new FileNoteRepository(directory.Path);
        var initial = NoteLibraryTestData.WithLinkedDocument(NoteLibraryTestData.Create(1));
        await repository.ReplaceLibraryAsync(initial);
        var pair = PdfContentLinks.Create(
            "doc-1",
            "sheet-1",
            12,
            new PdfAnnotationRect(10, 20, 110, 220));

        await Assert.ThrowsExactlyAsync<NoteRepositoryMutationException>(async () =>
            await repository.SaveLinkedSheetContentAsync(
                "sheet-1",
                new RtfSheetContent { Rtf = @"{\rtf1\ansi\pard Must not commit\par}" },
                pair.Link,
                pair.Relation with { SourceId = "missing-document" }));
        var reloaded = await repository.LoadLibraryAsync();

        Assert.IsNotNull(reloaded);
        NativeLibrarySnapshotVerifier.AssertEquivalent(initial, reloaded);
    }

    [TestMethod]
    public async Task HierarchyCrud_RepairsOrdersActiveStateAndTargetLinks()
    {
        using var directory = new TemporaryRepositoryDirectory();
        await using var repository = new FileNoteRepository(directory.Path);
        await repository.ReplaceLibraryAsync(NoteLibraryTestData.WithLinkedDocument(NoteLibraryTestData.Create()));
        var created = await repository.CreatePageAsync(
            new CreatePageRequest { Id = "page-2", SheetId = "sheet-3", SectionId = "section-2", Title = "Cường giáp" },
            new RtfSheetContent { Rtf = @"{\rtf1\ansi\pard Thiamazole\par}" });
        Assert.AreEqual("sheet-3", created.ActiveSheetId);
        await repository.CreateSheetAsync(
            new CreateSheetRequest { Id = "sheet-4", PageId = "page-2" },
            new RtfSheetContent { Rtf = @"{\rtf1\ansi\pard PTU\par}" });
        await repository.MoveSheetAsync("sheet-2", "page-2", 1);
        await repository.UpsertDocumentLinkAsync(new NoteDocumentLink
        {
            Id = "link-page-2",
            DocumentId = "doc-1",
            TargetType = DocumentLinkTargetType.Page,
            TargetId = "page-2",
        });
        await repository.UpsertDocumentLinkRelationAsync(new DocumentLinkRelation
        {
            Id = "relation-page-2",
            LinkIds = ["link-page-2"],
            Kind = DocumentRelationKind.Content,
            SourceType = DocumentRelationSourceType.Document,
            SourceId = "doc-1",
            CreatedAt = 1,
            UpdatedAt = 1,
        });

        await repository.DeletePageAsync("page-2");
        var library = await repository.LoadLibraryAsync();

        Assert.IsNotNull(library);
        Assert.IsFalse(library.Notes.Pages.Any(page => page.Id == "page-2"));
        Assert.IsFalse(library.Notes.Sheets.Any(sheet => sheet.Id is "sheet-2" or "sheet-3" or "sheet-4"));
        Assert.AreEqual("sheet-1", library.Notes.Active.ActiveSheetId);
        Assert.IsTrue(library.Documents.Links.Any(link => link.Id == "link-1"));
        Assert.IsFalse(library.Documents.Links.Any(link => link.Id == "link-page-2"));
        Assert.IsFalse(library.Documents.LinkRelations.Any(relation => relation.Id == "relation-page-2"));
        NoteLibraryValidator.AssertValid(library);
    }

    [TestMethod]
    public async Task DeleteLastSheet_IsRejectedWithoutChangingRepository()
    {
        using var directory = new TemporaryRepositoryDirectory();
        await using var repository = new FileNoteRepository(directory.Path);
        var expected = NoteLibraryTestData.Create(1);
        await repository.ReplaceLibraryAsync(expected);

        await Assert.ThrowsExactlyAsync<NoteRepositoryMutationException>(async () => await repository.DeleteSheetAsync("sheet-1"));
        var actual = await repository.LoadLibraryAsync();

        Assert.IsNotNull(actual);
        NativeLibrarySnapshotVerifier.AssertEquivalent(expected, actual);
    }

    [TestMethod]
    public async Task DocumentLinks_AreManyToManyAndDeletingDocumentKeepsNotes()
    {
        using var directory = new TemporaryRepositoryDirectory();
        await using var repository = new FileNoteRepository(directory.Path);
        var expectedNotes = NoteLibraryTestData.Create(2);
        await repository.ReplaceLibraryAsync(expectedNotes);
        await repository.UpsertDocumentAsync(new DocumentRecord
        {
            Id = "doc-a",
            Name = "ADA.pdf",
            Size = 1,
            LastModified = 1,
            Available = true,
            Payload = JsonValues.EmptyObject(),
        });
        await repository.UpsertDocumentAsync(new DocumentRecord
        {
            Id = "doc-b",
            Name = "EASD.pdf",
            Size = 2,
            LastModified = 2,
            Available = true,
            Payload = JsonValues.EmptyObject(),
        });
        await repository.UpsertDocumentLinkAsync(new NoteDocumentLink { Id = "a-page", DocumentId = "doc-a", TargetType = DocumentLinkTargetType.Page, TargetId = "page-1" });
        await repository.UpsertDocumentLinkAsync(new NoteDocumentLink { Id = "a-sheet", DocumentId = "doc-a", TargetType = DocumentLinkTargetType.Sheet, TargetId = "sheet-1" });
        await repository.UpsertDocumentLinkAsync(new NoteDocumentLink { Id = "b-page", DocumentId = "doc-b", TargetType = DocumentLinkTargetType.Page, TargetId = "page-1" });

        await repository.DeleteDocumentAsync("doc-a");
        var library = await repository.LoadLibraryAsync();

        Assert.IsNotNull(library);
        Assert.AreEqual(1, library.Documents.Documents.Count);
        Assert.AreEqual("doc-b", library.Documents.Documents.Single().Id);
        Assert.AreEqual(1, library.Documents.Links.Count);
        Assert.AreEqual("b-page", library.Documents.Links.Single().Id);
        CollectionAssert.AreEqual(expectedNotes.Notes.Pages.Select(page => page.Id).ToArray(), library.Notes.Pages.Select(page => page.Id).ToArray());
        CollectionAssert.AreEqual(expectedNotes.Notes.Sheets.Select(sheet => sheet.Id).ToArray(), library.Notes.Sheets.Select(sheet => sheet.Id).ToArray());
    }

    [TestMethod]
    public async Task SaveAndDeleteDocumentWorkspace_AreSingleManifestMutations()
    {
        using var directory = new TemporaryRepositoryDirectory();
        await using var repository = new FileNoteRepository(directory.Path);
        var notes = NoteLibraryTestData.Create(1);
        await repository.ReplaceLibraryAsync(notes);
        var graph = await repository.SaveDocumentWorkspaceAsync(new SaveDocumentWorkspaceRequest
        {
            Documents =
            [
                new DocumentRecord { Id = "doc", Name = "ADA.pdf", Available = true, Payload = JsonValues.EmptyObject() },
            ],
            Context = new DocumentContextRecord
            {
                Id = "context",
                Kind = "document",
                Name = "ADA",
                DocumentIds = ["doc"],
                ActiveDocumentId = "doc",
                SourcePage = 1,
            },
            Links =
            [
                new NoteDocumentLink { Id = "link", DocumentId = "doc", TargetType = DocumentLinkTargetType.Sheet, TargetId = "sheet-1" },
            ],
            LinkRelations =
            [
                new DocumentLinkRelation
                {
                    Id = "relation",
                    LinkIds = ["link"],
                    Kind = DocumentRelationKind.Workspace,
                    SourceType = DocumentRelationSourceType.Document,
                    SourceId = "doc",
                },
            ],
        });
        Assert.AreEqual(1, graph.Documents.Count);
        Assert.AreEqual(1, graph.Links.Count);

        var afterDelete = await repository.DeleteDocumentWorkspaceAsync("context");

        Assert.AreEqual(0, afterDelete.Documents.Count);
        Assert.AreEqual(0, afterDelete.Contexts.Count);
        Assert.AreEqual(0, afterDelete.Links.Count);
        Assert.AreEqual("page-1", (await repository.LoadNoteStructureAsync())?.Pages.Single().Id);
    }
}
