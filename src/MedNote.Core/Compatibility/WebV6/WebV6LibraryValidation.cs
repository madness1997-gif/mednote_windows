using System.Text.Json;

namespace MedNote.Core.Compatibility.WebV6;

public static class WebV6LibraryValidator
{
    private static readonly HashSet<string> NavigationContentFields = new(StringComparer.Ordinal)
    {
        "id",
        "title",
        "titleHtml",
        "pageId",
        "sectionId",
        "notebookId",
        "order",
        "logicalPageId",
        "logicalPageTitle",
        "sheetTitle",
        "sheetOrder",
        "__mednoteLazyPage",
    };

    public static IReadOnlyList<LibraryValidationIssue> Validate(WebLibraryV6 library)
    {
        ArgumentNullException.ThrowIfNull(library);
        var issues = new List<LibraryValidationIssue>();
        if (library.Version != WebV6Schema.Version)
        {
            issues.Add(new LibraryValidationIssue(
                "invalid-version",
                "library",
                string.Empty,
                $"Thư viện web phải dùng schema v{WebV6Schema.Version}."));
        }

        if (library.Notes is null || library.Documents is null || library.Preferences is null || library.SheetContents is null)
        {
            issues.Add(new LibraryValidationIssue(
                "missing-root",
                "library",
                string.Empty,
                "Thư viện web v6 thiếu notes, sheetContents, documents hoặc preferences."));
            return issues;
        }

        issues.AddRange(NoteLibraryValidator.ValidateMetadata(
            library.Notes,
            library.Documents,
            library.Preferences,
            library.SheetContents.Keys));
        foreach (var (sheetId, content) in library.SheetContents)
        {
            issues.AddRange(ValidateSheetContent(sheetId, content));
        }

        return issues;
    }

    public static IReadOnlyList<LibraryValidationIssue> ValidateSheetContent(string sheetId, JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object)
        {
            return
            [
                new LibraryValidationIssue(
                    "invalid-content",
                    "sheet-content",
                    sheetId,
                    $"SheetContent web {sheetId} phải là object JSON."),
            ];
        }

        var copiedNavigation = content.EnumerateObject()
            .Select(property => property.Name)
            .Where(NavigationContentFields.Contains)
            .ToList();
        return copiedNavigation.Count == 0
            ? []
            :
            [
                new LibraryValidationIssue(
                    "navigation-metadata-in-content",
                    "sheet-content",
                    sheetId,
                    $"SheetContent web {sheetId} chứa metadata điều hướng: {string.Join(", ", copiedNavigation)}."),
            ];
    }

    public static void AssertValid(WebLibraryV6 library)
    {
        var issues = Validate(library);
        if (issues.Count > 0)
        {
            throw new NoteLibraryValidationException(issues);
        }
    }
}
