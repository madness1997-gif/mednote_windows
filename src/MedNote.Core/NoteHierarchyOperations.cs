namespace MedNote.Core;

public static class NoteHierarchyOperations
{
    public static HierarchyMutation CreateNotebook(NoteStructure source, CreateNotebookRequest request)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        NoteLibraryValidator.AssertNoteStructureValid(source);

        var notebookId = request.Id ?? NewId("notebook");
        var sectionId = request.SectionId ?? NewId("section");
        var pageId = request.PageId ?? NewId("page");
        var sheetId = request.SheetId ?? NewId("sheet");
        EnsureIdsAvailable(source, notebookId, sectionId, pageId, sheetId);

        var notebook = new NotebookRecord
        {
            Id = notebookId,
            Title = CleanTitle(request.Title, "Sổ ghi chú"),
            Order = source.Notebooks.Count,
        };
        var section = new SectionRecord
        {
            Id = sectionId,
            NotebookId = notebookId,
            Title = CleanTitle(request.SectionTitle, "Phần 1"),
            Order = 0,
        };
        var page = new PageRecord
        {
            Id = pageId,
            SectionId = sectionId,
            Title = CleanTitle(request.PageTitle, "Page 1"),
            Order = 0,
        };
        var sheet = new SheetRecord { Id = sheetId, PageId = pageId, Order = 0 };
        var notes = source with
        {
            Notebooks = [.. source.Notebooks, notebook],
            Sections = [.. source.Sections, section],
            Pages = [.. source.Pages, page],
            Sheets = [.. source.Sheets, sheet],
            Active = new ActiveNoteState
            {
                ActiveNotebookId = notebookId,
                ActiveSectionId = sectionId,
                ActivePageId = pageId,
                ActiveSheetId = sheetId,
            },
        };
        NoteLibraryValidator.AssertNoteStructureValid(notes);
        return new HierarchyMutation(notes, [sheetId], []);
    }

    public static HierarchyMutation CreateSection(NoteStructure source, CreateSectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        NoteLibraryValidator.AssertNoteStructureValid(source);
        if (!source.Notebooks.Any(record => record.Id == request.NotebookId))
        {
            throw new NoteRepositoryMutationException($"Không tìm thấy Notebook {request.NotebookId}.");
        }

        var id = request.Id ?? NewId("section");
        EnsureUnique(source.Sections.Select(record => record.Id), id, "Section");
        var section = new SectionRecord
        {
            Id = id,
            NotebookId = request.NotebookId,
            Title = CleanTitle(request.Title, "Phần mới"),
            Order = source.Sections.Count(record => record.NotebookId == request.NotebookId),
        };
        var notes = source with { Sections = [.. source.Sections, section] };
        NoteLibraryValidator.AssertNoteStructureValid(notes);
        return new HierarchyMutation(notes, [], []);
    }

    public static HierarchyMutation CreatePage(NoteStructure source, CreatePageRequest request)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        NoteLibraryValidator.AssertNoteStructureValid(source);
        var section = source.Sections.FirstOrDefault(record => record.Id == request.SectionId)
            ?? throw new NoteRepositoryMutationException($"Không tìm thấy Section {request.SectionId}.");
        var pageId = request.Id ?? NewId("page");
        var sheetId = request.SheetId ?? NewId("sheet");
        EnsureUnique(source.Pages.Select(record => record.Id), pageId, "Page");
        EnsureUnique(source.Sheets.Select(record => record.Id), sheetId, "Sheet");

        var page = new PageRecord
        {
            Id = pageId,
            SectionId = request.SectionId,
            Title = CleanTitle(request.Title, "Page mới"),
            Order = source.Pages.Count(record => record.SectionId == request.SectionId),
        };
        var sheet = new SheetRecord { Id = sheetId, PageId = pageId, Order = 0 };
        var notes = source with
        {
            Pages = [.. source.Pages, page],
            Sheets = [.. source.Sheets, sheet],
            Active = new ActiveNoteState
            {
                ActiveNotebookId = section.NotebookId,
                ActiveSectionId = section.Id,
                ActivePageId = pageId,
                ActiveSheetId = sheetId,
            },
        };
        NoteLibraryValidator.AssertNoteStructureValid(notes);
        return new HierarchyMutation(notes, [sheetId], []);
    }

    public static HierarchyMutation CreateSheet(NoteStructure source, CreateSheetRequest request)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        NoteLibraryValidator.AssertNoteStructureValid(source);
        var page = source.Pages.FirstOrDefault(record => record.Id == request.PageId)
            ?? throw new NoteRepositoryMutationException($"Không tìm thấy Page {request.PageId}.");
        var section = source.Sections.Single(record => record.Id == page.SectionId);
        var id = request.Id ?? NewId("sheet");
        EnsureUnique(source.Sheets.Select(record => record.Id), id, "Sheet");
        var sheet = new SheetRecord
        {
            Id = id,
            PageId = page.Id,
            Order = source.Sheets.Count(record => record.PageId == page.Id),
        };
        var notes = source with
        {
            Sheets = [.. source.Sheets, sheet],
            Active = new ActiveNoteState
            {
                ActiveNotebookId = section.NotebookId,
                ActiveSectionId = section.Id,
                ActivePageId = page.Id,
                ActiveSheetId = sheet.Id,
            },
        };
        NoteLibraryValidator.AssertNoteStructureValid(notes);
        return new HierarchyMutation(notes, [id], []);
    }

    public static NoteStructure RenameNotebook(NoteStructure source, string id, string title) =>
        Rename(
            source,
            id,
            title,
            source.Notebooks,
            (notes, records) => notes with { Notebooks = records },
            (record, nextTitle) => record with { Title = CleanTitle(nextTitle, record.Title) },
            "Notebook");

    public static NoteStructure RenameSection(NoteStructure source, string id, string title) =>
        Rename(
            source,
            id,
            title,
            source.Sections,
            (notes, records) => notes with { Sections = records },
            (record, nextTitle) => record with { Title = CleanTitle(nextTitle, record.Title) },
            "Section");

    public static NoteStructure RenamePage(NoteStructure source, string id, string title) =>
        Rename(
            source,
            id,
            title,
            source.Pages,
            (notes, records) => notes with { Pages = records },
            (record, nextTitle) => record with { Title = nextTitle.Trim() },
            "Page");

    public static NoteStructure MovePage(NoteStructure source, string id, string sectionId, int order)
    {
        NoteLibraryValidator.AssertNoteStructureValid(source);
        var moving = source.Pages.FirstOrDefault(record => record.Id == id)
            ?? throw new NoteRepositoryMutationException($"Không tìm thấy Page {id}.");
        var targetSection = source.Sections.FirstOrDefault(record => record.Id == sectionId)
            ?? throw new NoteRepositoryMutationException($"Không tìm thấy Section {sectionId}.");
        var pages = ReorderMove(
            source.Pages,
            moving,
            record => record.SectionId,
            sectionId,
            order,
            (record, parentId, nextOrder) => record with { SectionId = parentId, Order = nextOrder });
        var active = source.Active.ActivePageId == id
            ? source.Active with { ActiveNotebookId = targetSection.NotebookId, ActiveSectionId = targetSection.Id }
            : source.Active;
        var notes = source with { Pages = pages, Active = active };
        NoteLibraryValidator.AssertNoteStructureValid(notes);
        return notes;
    }

    public static NoteStructure MoveSheet(NoteStructure source, string id, string pageId, int order)
    {
        NoteLibraryValidator.AssertNoteStructureValid(source);
        var moving = source.Sheets.FirstOrDefault(record => record.Id == id)
            ?? throw new NoteRepositoryMutationException($"Không tìm thấy Sheet {id}.");
        var targetPage = source.Pages.FirstOrDefault(record => record.Id == pageId)
            ?? throw new NoteRepositoryMutationException($"Không tìm thấy Page {pageId}.");
        if (moving.PageId != pageId && source.Sheets.Count(record => record.PageId == moving.PageId) == 1)
        {
            throw new NoteRepositoryMutationException("Không thể chuyển Sheet duy nhất vì Page nguồn phải luôn có ít nhất một Sheet.");
        }

        var sheets = ReorderMove(
            source.Sheets,
            moving,
            record => record.PageId,
            pageId,
            order,
            (record, parentId, nextOrder) => record with { PageId = parentId, Order = nextOrder });
        var active = source.Active;
        if (active.ActiveSheetId == id)
        {
            var section = source.Sections.Single(record => record.Id == targetPage.SectionId);
            active = new ActiveNoteState
            {
                ActiveNotebookId = section.NotebookId,
                ActiveSectionId = section.Id,
                ActivePageId = targetPage.Id,
                ActiveSheetId = id,
            };
        }

        var notes = source with { Sheets = sheets, Active = active };
        NoteLibraryValidator.AssertNoteStructureValid(notes);
        return notes;
    }

    public static HierarchyMutation DeleteNotebook(NoteStructure source, string id)
    {
        if (!source.Notebooks.Any(record => record.Id == id))
        {
            throw new NoteRepositoryMutationException($"Không tìm thấy Notebook {id}.");
        }

        var sectionIds = source.Sections.Where(record => record.NotebookId == id).Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
        var pageIds = source.Pages.Where(record => sectionIds.Contains(record.SectionId)).Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
        return Delete(
            source,
            new HashSet<string>(StringComparer.Ordinal) { id },
            sectionIds,
            pageIds);
    }

    public static HierarchyMutation DeleteSection(NoteStructure source, string id)
    {
        var section = source.Sections.FirstOrDefault(record => record.Id == id)
            ?? throw new NoteRepositoryMutationException($"Không tìm thấy Section {id}.");
        if (source.Sections.Count(record => record.NotebookId == section.NotebookId) <= 1)
        {
            throw new NoteRepositoryMutationException("Notebook phải luôn có ít nhất một Section.");
        }

        var pageIds = source.Pages.Where(record => record.SectionId == id).Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
        return Delete(
            source,
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal) { id },
            pageIds);
    }

    public static HierarchyMutation DeletePage(NoteStructure source, string id)
    {
        if (!source.Pages.Any(record => record.Id == id))
        {
            throw new NoteRepositoryMutationException($"Không tìm thấy Page {id}.");
        }

        return Delete(
            source,
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal) { id });
    }

    public static HierarchyMutation DeleteSheet(NoteStructure source, string id)
    {
        NoteLibraryValidator.AssertNoteStructureValid(source);
        var sheet = source.Sheets.FirstOrDefault(record => record.Id == id)
            ?? throw new NoteRepositoryMutationException($"Không tìm thấy Sheet {id}.");
        if (source.Sheets.Count(record => record.PageId == sheet.PageId) <= 1)
        {
            throw new NoteRepositoryMutationException("Page phải luôn có ít nhất một Sheet.");
        }

        var sheets = NormalizeOrders(source.Sheets.Where(record => record.Id != id), record => record.PageId, (record, order) => record with { Order = order });
        var notes = source with { Sheets = sheets };
        notes = notes with { Active = RepairActive(notes, source.Active.ActiveSheetId) };
        NoteLibraryValidator.AssertNoteStructureValid(notes);
        return new HierarchyMutation(notes, [], [id]);
    }

    public static NoteStructure SetActive(NoteStructure source, ActiveNoteState active)
    {
        var notes = source with { Active = active };
        NoteLibraryValidator.AssertNoteStructureValid(notes);
        return notes;
    }

    public static ActiveNoteState ActiveForSheet(NoteStructure source, string sheetId)
    {
        var sheet = source.Sheets.FirstOrDefault(record => record.Id == sheetId)
            ?? throw new NoteRepositoryMutationException($"Không tìm thấy Sheet {sheetId}.");
        var page = source.Pages.Single(record => record.Id == sheet.PageId);
        var section = source.Sections.Single(record => record.Id == page.SectionId);
        return new ActiveNoteState
        {
            ActiveNotebookId = section.NotebookId,
            ActiveSectionId = section.Id,
            ActivePageId = page.Id,
            ActiveSheetId = sheet.Id,
        };
    }

    private static HierarchyMutation Delete(
        NoteStructure source,
        IReadOnlySet<string> notebookIds,
        IReadOnlySet<string> sectionIds,
        IReadOnlySet<string> pageIds)
    {
        NoteLibraryValidator.AssertNoteStructureValid(source);
        var sheetIds = source.Sheets.Where(record => pageIds.Contains(record.PageId)).Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
        var notes = source with
        {
            Notebooks = NormalizeOrders(source.Notebooks.Where(record => !notebookIds.Contains(record.Id)), _ => string.Empty, (record, order) => record with { Order = order }),
            Sections = NormalizeOrders(source.Sections.Where(record => !sectionIds.Contains(record.Id)), record => record.NotebookId, (record, order) => record with { Order = order }),
            Pages = NormalizeOrders(source.Pages.Where(record => !pageIds.Contains(record.Id)), record => record.SectionId, (record, order) => record with { Order = order }),
            Sheets = source.Sheets.Where(record => !sheetIds.Contains(record.Id)).ToList(),
        };
        notes = notes with { Active = RepairActive(notes, source.Active.ActiveSheetId) };
        NoteLibraryValidator.AssertNoteStructureValid(notes);
        return new HierarchyMutation(notes, [], sheetIds.ToList());
    }

    private static ActiveNoteState RepairActive(NoteStructure notes, string preferredSheetId)
    {
        var sheetId = notes.Sheets.Any(record => record.Id == preferredSheetId)
            ? preferredSheetId
            : notes.Sheets.Select(record => record.Id).FirstOrDefault();
        return sheetId is null ? new ActiveNoteState() : ActiveForSheet(notes, sheetId);
    }

    private static NoteStructure Rename<T>(
        NoteStructure source,
        string id,
        string title,
        IReadOnlyList<T> records,
        Func<NoteStructure, List<T>, NoteStructure> apply,
        Func<T, string, T> rename,
        string label)
        where T : class
    {
        NoteLibraryValidator.AssertNoteStructureValid(source);
        var found = false;
        var next = records.Select(record =>
        {
            var recordId = record switch
            {
                NotebookRecord notebook => notebook.Id,
                SectionRecord section => section.Id,
                PageRecord page => page.Id,
                _ => string.Empty,
            };
            if (recordId != id)
            {
                return record;
            }

            found = true;
            return rename(record, title);
        }).ToList();
        if (!found)
        {
            throw new NoteRepositoryMutationException($"Không tìm thấy {label} {id}.");
        }

        var notes = apply(source, next);
        NoteLibraryValidator.AssertNoteStructureValid(notes);
        return notes;
    }

    private static List<T> ReorderMove<T>(
        IReadOnlyList<T> records,
        T moving,
        Func<T, string> parent,
        string nextParent,
        int nextOrder,
        Func<T, string, int, T> update)
        where T : class
    {
        var movingId = IdOf(moving);
        var oldParent = parent(moving);
        var oldSiblings = records.Where(record => parent(record) == oldParent && IdOf(record) != movingId).OrderBy(record => OrderOf(record)).ToList();
        var destination = oldParent == nextParent
            ? oldSiblings
            : records.Where(record => parent(record) == nextParent && IdOf(record) != movingId).OrderBy(record => OrderOf(record)).ToList();
        destination.Insert(Math.Clamp(nextOrder, 0, destination.Count), moving);

        var changed = records.ToDictionary(record => IdOf(record), record => record, StringComparer.Ordinal);
        if (oldParent != nextParent)
        {
            for (var index = 0; index < oldSiblings.Count; index++)
            {
                changed[IdOf(oldSiblings[index])] = update(oldSiblings[index], oldParent, index);
            }
        }

        for (var index = 0; index < destination.Count; index++)
        {
            changed[IdOf(destination[index])] = update(destination[index], nextParent, index);
        }

        return records.Select(record => changed[IdOf(record)]).ToList();
    }

    private static List<T> NormalizeOrders<T>(
        IEnumerable<T> records,
        Func<T, string> parent,
        Func<T, int, T> update)
    {
        return records
            .GroupBy(parent, StringComparer.Ordinal)
            .SelectMany(group => group.OrderBy(record => OrderOf(record)).Select((record, order) => update(record, order)))
            .ToList();
    }

    private static string IdOf<T>(T record) => record switch
    {
        NotebookRecord notebook => notebook.Id,
        SectionRecord section => section.Id,
        PageRecord page => page.Id,
        SheetRecord sheet => sheet.Id,
        _ => throw new ArgumentException($"Không hỗ trợ record {typeof(T).Name}."),
    };

    private static int OrderOf<T>(T record) => record switch
    {
        NotebookRecord notebook => notebook.Order,
        SectionRecord section => section.Order,
        PageRecord page => page.Order,
        SheetRecord sheet => sheet.Order,
        _ => throw new ArgumentException($"Không hỗ trợ record {typeof(T).Name}."),
    };

    private static void EnsureIdsAvailable(NoteStructure source, string notebookId, string sectionId, string pageId, string sheetId)
    {
        EnsureUnique(source.Notebooks.Select(record => record.Id), notebookId, "Notebook");
        EnsureUnique(source.Sections.Select(record => record.Id), sectionId, "Section");
        EnsureUnique(source.Pages.Select(record => record.Id), pageId, "Page");
        EnsureUnique(source.Sheets.Select(record => record.Id), sheetId, "Sheet");
    }

    private static void EnsureUnique(IEnumerable<string> ids, string id, string label)
    {
        if (string.IsNullOrWhiteSpace(id) || ids.Contains(id, StringComparer.Ordinal))
        {
            throw new NoteRepositoryMutationException($"ID tạo {label} không hợp lệ hoặc đã tồn tại: {id}.");
        }
    }

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static string CleanTitle(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
