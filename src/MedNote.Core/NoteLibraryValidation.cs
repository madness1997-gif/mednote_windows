using System.Text.Json;

namespace MedNote.Core;

public sealed record LibraryValidationIssue(string Code, string Entity, string Id, string Message);

public sealed class NoteLibraryValidationException : IOException
{
    public NoteLibraryValidationException(IReadOnlyList<LibraryValidationIssue> issues)
        : base(string.Join("; ", issues.Select(issue => issue.Message)))
    {
        Issues = issues;
    }

    public IReadOnlyList<LibraryValidationIssue> Issues { get; }
}

public static class NoteLibraryValidator
{
    public static IReadOnlyList<LibraryValidationIssue> Validate(NativeLibrarySnapshot library)
    {
        ArgumentNullException.ThrowIfNull(library);
        var issues = new List<LibraryValidationIssue>();
        if (library.Version != NativeNoteSchema.Version)
        {
            issues.Add(Issue("invalid-version", "library", string.Empty, $"Thư viện native phải dùng schema v{NativeNoteSchema.Version}."));
        }

        if (library.Notes is null || library.Documents is null || library.Preferences is null || library.SheetContents is null)
        {
            issues.Add(Issue("missing-root", "library", string.Empty, "Thư viện native thiếu notes, sheetContents, documents hoặc preferences."));
            return issues;
        }

        issues.AddRange(ValidateMetadata(library.Notes, library.Documents, library.Preferences, library.SheetContents.Keys));
        foreach (var (sheetId, content) in library.SheetContents)
        {
            issues.AddRange(ValidateSheetContent(sheetId, content));
        }

        return issues;
    }

    public static IReadOnlyList<LibraryValidationIssue> ValidateMetadata(
        NoteStructure notes,
        DocumentGraph documents,
        LibraryPreferences preferences,
        IEnumerable<string> sheetContentIds)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(sheetContentIds);

        var issues = new List<LibraryValidationIssue>();
        issues.AddRange(ValidateNoteStructure(notes));
        if (!HasRequiredNoteMembers(notes))
        {
            return issues;
        }

        issues.AddRange(ValidateDocumentGraph(documents, notes));

        if (!double.IsFinite(preferences.ReaderShare)
            || preferences.NoteZoom is double noteZoom && !double.IsFinite(noteZoom))
        {
            issues.Add(Issue("invalid-preferences", "preferences", string.Empty, "Metadata điều hướng trong Library không hợp lệ."));
        }

        var expected = notes.Sheets.Select(sheet => sheet.Id).ToHashSet(StringComparer.Ordinal);
        var actualList = sheetContentIds.ToList();
        var actual = actualList.ToHashSet(StringComparer.Ordinal);
        foreach (var duplicate in actualList.GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            issues.Add(Issue("duplicate-content", "sheet-content", duplicate.Key, $"SheetContent {duplicate.Key} xuất hiện nhiều lần."));
        }

        foreach (var id in expected.Except(actual, StringComparer.Ordinal))
        {
            issues.Add(Issue("missing-content", "sheet-content", id, $"Sheet {id} thiếu SheetContent record."));
        }

        foreach (var id in actual.Except(expected, StringComparer.Ordinal))
        {
            issues.Add(Issue("orphan-content", "sheet-content", id, $"SheetContent {id} không có Sheet metadata tương ứng."));
        }

        return issues;
    }

    public static IReadOnlyList<LibraryValidationIssue> ValidateNoteStructure(NoteStructure graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var issues = new List<LibraryValidationIssue>();
        if (!HasRequiredNoteMembers(graph))
        {
            issues.Add(Issue("missing-note-root", "workspace", string.Empty, "NoteStructure thiếu workspace, hierarchy hoặc active state."));
            return issues;
        }

        AddDuplicateIssues(graph.Notebooks, record => record.Id, "notebook", issues);
        AddDuplicateIssues(graph.Sections, record => record.Id, "section", issues);
        AddDuplicateIssues(graph.Pages, record => record.Id, "page", issues);
        AddDuplicateIssues(graph.Sheets, record => record.Id, "sheet", issues);

        var notebooks = graph.Notebooks.ToDictionarySafely(record => record.Id);
        var sections = graph.Sections.ToDictionarySafely(record => record.Id);
        var pages = graph.Pages.ToDictionarySafely(record => record.Id);
        var sheets = graph.Sheets.ToDictionarySafely(record => record.Id);

        foreach (var section in graph.Sections.Where(section => !notebooks.ContainsKey(section.NotebookId)))
        {
            issues.Add(Issue("missing-parent", "section", section.Id, $"Section {section.Id} không có Notebook {section.NotebookId}."));
        }

        foreach (var page in graph.Pages.Where(page => !sections.ContainsKey(page.SectionId)))
        {
            issues.Add(Issue("missing-parent", "page", page.Id, $"Page {page.Id} không có Section {page.SectionId}."));
        }

        foreach (var sheet in graph.Sheets.Where(sheet => !pages.ContainsKey(sheet.PageId)))
        {
            issues.Add(Issue("missing-parent", "sheet", sheet.Id, $"Sheet {sheet.Id} không có Page {sheet.PageId}."));
        }

        foreach (var notebook in graph.Notebooks.Where(notebook => !graph.Sections.Any(section => section.NotebookId == notebook.Id)))
        {
            issues.Add(Issue("empty-notebook", "notebook", notebook.Id, $"Notebook {notebook.Id} phải có ít nhất một Section."));
        }

        foreach (var page in graph.Pages.Where(page => !graph.Sheets.Any(sheet => sheet.PageId == page.Id)))
        {
            issues.Add(Issue("empty-page", "page", page.Id, $"Page {page.Id} phải có ít nhất một Sheet."));
        }

        AddOrderIssues<NotebookRecord>([graph.Notebooks], record => record.Id, record => record.Order, "notebook", issues);
        AddOrderIssues<SectionRecord>(graph.Sections.GroupBy(record => record.NotebookId), record => record.Id, record => record.Order, "section", issues);
        AddOrderIssues<PageRecord>(graph.Pages.GroupBy(record => record.SectionId), record => record.Id, record => record.Order, "page", issues);
        AddOrderIssues<SheetRecord>(graph.Sheets.GroupBy(record => record.PageId), record => record.Id, record => record.Order, "sheet", issues);

        var active = graph.Active;
        if (graph.Sheets.Count == 0)
        {
            if (!IsEmpty(active))
            {
                issues.Add(Issue("invalid-active-chain", "active", active.ActiveSheetId, "Active state phải rỗng khi thư viện không có Sheet."));
            }

            return issues;
        }

        var hasChain = notebooks.TryGetValue(active.ActiveNotebookId, out var activeNotebook)
            && sections.TryGetValue(active.ActiveSectionId, out var activeSection)
            && pages.TryGetValue(active.ActivePageId, out var activePage)
            && sheets.TryGetValue(active.ActiveSheetId, out var activeSheet)
            && activeSection.NotebookId == activeNotebook.Id
            && activePage.SectionId == activeSection.Id
            && activeSheet.PageId == activePage.Id;
        if (!hasChain)
        {
            issues.Add(Issue("invalid-active-chain", "active", active.ActiveSheetId, "Bốn active ID phải tạo thành chuỗi Notebook → Section → Page → Sheet hợp lệ."));
        }

        return issues;
    }

    public static IReadOnlyList<LibraryValidationIssue> ValidateDocumentGraph(DocumentGraph graph, NoteStructure notes)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(notes);
        var issues = new List<LibraryValidationIssue>();
        if (graph.Documents is null
            || graph.Contexts is null
            || graph.Groups is null
            || graph.Links is null
            || graph.LinkRelations is null)
        {
            issues.Add(Issue("missing-document-root", "document", string.Empty, "DocumentGraph thiếu một hoặc nhiều record collections."));
            return issues;
        }

        AddDuplicateIssues(graph.Documents, record => record.Id, "document", issues);
        AddDuplicateIssues(graph.Contexts, record => record.Id, "context", issues);
        AddDuplicateIssues(graph.Groups, record => record.Id, "group", issues);
        AddDuplicateIssues(graph.Links, record => record.Id, "link", issues);
        AddDuplicateIssues(graph.LinkRelations, record => record.Id, "link-relation", issues);

        var documents = graph.Documents.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
        var groups = graph.Groups.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
        var links = graph.Links.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
        var pages = notes.Pages.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
        var sheets = notes.Sheets.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var document in graph.Documents.Where(document => document.Payload.ValueKind != JsonValueKind.Object))
        {
            issues.Add(Issue("invalid-payload", "document", document.Id, $"Document {document.Id} phải có payload dạng object."));
        }

        foreach (var context in graph.Contexts)
        {
            var invalid = context.DocumentIds is null
                || context.DocumentIds.Any(id => !documents.Contains(id))
                || context.ActiveDocumentId is not null && !context.DocumentIds.Contains(context.ActiveDocumentId, StringComparer.Ordinal);
            if (invalid)
            {
                issues.Add(Issue("invalid-context", "context", context.Id, $"DocumentContext {context.Id} có document reference không hợp lệ."));
            }
        }

        foreach (var group in graph.Groups)
        {
            if (group.DocumentIds is null)
            {
                issues.Add(Issue("invalid-group", "group", group.Id, $"Group {group.Id} thiếu documentIds."));
                continue;
            }

            foreach (var missing in group.DocumentIds.Where(id => !documents.Contains(id)))
            {
                issues.Add(Issue("missing-document", "group", group.Id, $"Group {group.Id} không có Document {missing}."));
            }
        }

        foreach (var link in graph.Links)
        {
            if (!documents.Contains(link.DocumentId))
            {
                issues.Add(Issue("missing-document", "link", link.Id, $"Link {link.Id} không có Document {link.DocumentId}."));
            }

            var targetExists = link.TargetType == DocumentLinkTargetType.Page
                ? pages.Contains(link.TargetId)
                : sheets.Contains(link.TargetId);
            if (!targetExists)
            {
                issues.Add(Issue("missing-target", "link", link.Id, $"Link {link.Id} không có {link.TargetType.ToString().ToLowerInvariant()} {link.TargetId}."));
            }
        }

        foreach (var relation in graph.LinkRelations)
        {
            if (relation.LinkIds is null)
            {
                issues.Add(Issue("invalid-link-relation", "link-relation", relation.Id, $"Link relation {relation.Id} thiếu linkIds."));
                continue;
            }

            foreach (var missing in relation.LinkIds.Where(id => !links.Contains(id)))
            {
                issues.Add(Issue("missing-link", "link-relation", relation.Id, $"Link relation {relation.Id} không có core link {missing}."));
            }

            var sourceExists = relation.SourceType == DocumentRelationSourceType.Document
                ? documents.Contains(relation.SourceId)
                : groups.Contains(relation.SourceId);
            if (!sourceExists)
            {
                var isDocument = relation.SourceType == DocumentRelationSourceType.Document;
                var source = isDocument ? "Document" : "Group";
                issues.Add(Issue(isDocument ? "missing-document" : "missing-group", "link-relation", relation.Id, $"Link relation {relation.Id} không có {source} nguồn {relation.SourceId}."));
            }
        }

        return issues;
    }

    public static IReadOnlyList<LibraryValidationIssue> ValidateSheetContent(string sheetId, RtfSheetContent content)
    {
        var issues = new List<LibraryValidationIssue>();
        if (content is null || !RtfDocument.IsRtf(content.Rtf))
        {
            issues.Add(Issue("invalid-content", "sheet-content", sheetId, $"SheetContent {sheetId} phải chứa tài liệu RTF hợp lệ."));
        }

        return issues;
    }

    public static void AssertValid(NativeLibrarySnapshot library) => ThrowIfAny(Validate(library));

    public static void AssertMetadataValid(
        NoteStructure notes,
        DocumentGraph documents,
        LibraryPreferences preferences,
        IEnumerable<string> sheetContentIds) =>
        ThrowIfAny(ValidateMetadata(notes, documents, preferences, sheetContentIds));

    public static void AssertNoteStructureValid(NoteStructure notes) => ThrowIfAny(ValidateNoteStructure(notes));

    public static void AssertDocumentGraphValid(DocumentGraph graph, NoteStructure notes) => ThrowIfAny(ValidateDocumentGraph(graph, notes));

    public static void AssertSheetContentValid(string sheetId, RtfSheetContent content) => ThrowIfAny(ValidateSheetContent(sheetId, content));

    private static void ThrowIfAny(IReadOnlyList<LibraryValidationIssue> issues)
    {
        if (issues.Count > 0)
        {
            throw new NoteLibraryValidationException(issues);
        }
    }

    private static bool IsEmpty(ActiveNoteState active) =>
        string.IsNullOrEmpty(active.ActiveNotebookId)
        && string.IsNullOrEmpty(active.ActiveSectionId)
        && string.IsNullOrEmpty(active.ActivePageId)
        && string.IsNullOrEmpty(active.ActiveSheetId);

    private static bool HasRequiredNoteMembers(NoteStructure graph) =>
        graph.Workspace is not null
        && graph.Notebooks is not null
        && graph.Sections is not null
        && graph.Pages is not null
        && graph.Sheets is not null
        && graph.Active is not null;

    private static void AddDuplicateIssues<T>(
        IEnumerable<T> records,
        Func<T, string> id,
        string entity,
        ICollection<LibraryValidationIssue> issues)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            var recordId = id(record);
            if (string.IsNullOrEmpty(recordId) || !seen.Add(recordId))
            {
                issues.Add(Issue("duplicate-id", entity, recordId, $"{entity} phải có ID thật và duy nhất: {(string.IsNullOrEmpty(recordId) ? "(rỗng)" : recordId)}."));
            }
        }
    }

    private static void AddOrderIssues<T>(
        IEnumerable<IEnumerable<T>> groups,
        Func<T, string> id,
        Func<T, int> order,
        string entity,
        ICollection<LibraryValidationIssue> issues)
    {
        foreach (var group in groups)
        {
            var ordered = group.OrderBy(order).ToList();
            for (var index = 0; index < ordered.Count; index++)
            {
                var record = ordered[index];
                if (order(record) != index)
                {
                    issues.Add(Issue("invalid-order", entity, id(record), $"{entity} {id(record)} có order {order(record)}; cần liên tục từ 0 trong cùng parent."));
                }
            }
        }
    }

    private static LibraryValidationIssue Issue(string code, string entity, string id, string message) => new(code, entity, id, message);

    private static Dictionary<string, T> ToDictionarySafely<T>(this IEnumerable<T> records, Func<T, string> id)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            var key = id(record);
            if (!string.IsNullOrEmpty(key))
            {
                result.TryAdd(key, record);
            }
        }

        return result;
    }
}
