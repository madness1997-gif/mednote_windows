using MedNote.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class NoteHierarchyOperationsTests
{
    [TestMethod]
    public void CreateNotebook_CreatesCompleteActiveChain()
    {
        var source = new NoteStructure
        {
            Workspace = new WorkspaceRecord { Id = "workspace", Title = "MedNote" },
        };

        var mutation = NoteHierarchyOperations.CreateNotebook(source, new CreateNotebookRequest
        {
            Id = "notebook",
            SectionId = "section",
            PageId = "page",
            SheetId = "sheet",
            Title = "Nội tiết",
        });

        Assert.AreEqual("sheet", mutation.Notes.Active.ActiveSheetId);
        CollectionAssert.AreEqual(new[] { "sheet" }, mutation.CreatedSheetIds.ToArray());
        NoteLibraryValidator.AssertNoteStructureValid(mutation.Notes);
    }

    [TestMethod]
    public void MovePage_RepairsBothSiblingOrdersAndActiveParents()
    {
        var source = NoteLibraryTestData.Create(1).Notes;
        var created = NoteHierarchyOperations.CreatePage(source, new CreatePageRequest
        {
            Id = "page-2",
            SheetId = "sheet-2",
            SectionId = "section-1",
            Title = "Page 2",
        }).Notes;

        var moved = NoteHierarchyOperations.MovePage(created, "page-2", "section-2", 0);

        Assert.AreEqual("section-2", moved.Pages.Single(page => page.Id == "page-2").SectionId);
        Assert.AreEqual(0, moved.Pages.Single(page => page.Id == "page-2").Order);
        Assert.AreEqual("section-2", moved.Active.ActiveSectionId);
        Assert.AreEqual("notebook-1", moved.Active.ActiveNotebookId);
        NoteLibraryValidator.AssertNoteStructureValid(moved);
    }

    [TestMethod]
    public void SectionAndNotebookCrud_PreservesInvariants()
    {
        var source = NoteLibraryTestData.Create(1).Notes;
        var sectionMutation = NoteHierarchyOperations.CreateSection(source, new CreateSectionRequest
        {
            Id = "section-3",
            NotebookId = "notebook-1",
            Title = "Tuyến yên",
        });
        var renamed = NoteHierarchyOperations.RenameSection(sectionMutation.Notes, "section-3", "Tuyến yên – nước");
        var deletedSection = NoteHierarchyOperations.DeleteSection(renamed, "section-3");
        var notebookMutation = NoteHierarchyOperations.CreateNotebook(deletedSection.Notes, new CreateNotebookRequest
        {
            Id = "notebook-2",
            SectionId = "section-4",
            PageId = "page-2",
            SheetId = "sheet-2",
            Title = "Tim mạch",
        });
        var renamedNotebook = NoteHierarchyOperations.RenameNotebook(notebookMutation.Notes, "notebook-2", "Tim mạch học");
        var deletedNotebook = NoteHierarchyOperations.DeleteNotebook(renamedNotebook, "notebook-2");

        Assert.AreEqual(1, deletedNotebook.Notes.Notebooks.Count);
        Assert.AreEqual("notebook-1", deletedNotebook.Notes.Notebooks.Single().Id);
        CollectionAssert.AreEqual(new[] { "sheet-2" }, deletedNotebook.RemovedSheetIds.ToArray());
        NoteLibraryValidator.AssertNoteStructureValid(deletedNotebook.Notes);
    }

    [TestMethod]
    public void DeleteOnlySection_IsRejected()
    {
        var source = NoteLibraryTestData.Create(1).Notes;
        var withoutSecond = NoteHierarchyOperations.DeleteSection(source, "section-2").Notes;

        Assert.ThrowsExactly<NoteRepositoryMutationException>(() => NoteHierarchyOperations.DeleteSection(withoutSecond, "section-1"));
    }
}
