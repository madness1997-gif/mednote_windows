using MedNote.Core;
using MedNote.Core.Compatibility.WebV6;

namespace MedNote.Infrastructure.Compatibility.WebV6;

public sealed record WebV6ImportResult
{
    public int NotebookCount { get; init; }

    public int SectionCount { get; init; }

    public int PageCount { get; init; }

    public int SheetCount { get; init; }

    public int DocumentCount { get; init; }

    public int LinkCount { get; init; }

    public Dictionary<string, string> WebSheetContentHashes { get; init; } = [];
}

/// <summary>
/// Compatibility boundary for one-way web-v6 imports. Conversion completes
/// before the native repository starts its staged, atomic replacement.
/// </summary>
public sealed class WebV6LibraryImporter(INoteRepository repository)
{
    private readonly INoteRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async ValueTask<WebV6ImportResult> ImportAsync(
        Stream payload,
        IWebV6SheetContentConverter converter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(converter);
        using var buffer = new MemoryStream();
        await payload.CopyToAsync(buffer, cancellationToken);
        var webLibrary = WebV6Backup.Parse(buffer.ToArray());
        var nativeContents = new Dictionary<string, RtfSheetContent>(StringComparer.Ordinal);
        foreach (var sheet in webLibrary.Notes.Sheets)
        {
            var nativeContent = await converter.ConvertAsync(
                sheet.Id,
                webLibrary.SheetContents[sheet.Id],
                cancellationToken);
            NoteLibraryValidator.AssertSheetContentValid(sheet.Id, nativeContent);
            nativeContents.Add(sheet.Id, nativeContent);
        }

        var nativeLibrary = new NativeLibrarySnapshot
        {
            Notes = webLibrary.Notes,
            SheetContents = nativeContents,
            Documents = webLibrary.Documents,
            Preferences = webLibrary.Preferences,
            SavedAt = webLibrary.SavedAt,
            ExtensionData = webLibrary.ExtensionData,
        };
        NoteLibraryValidator.AssertValid(nativeLibrary);
        await _repository.ReplaceLibraryAsync(nativeLibrary, cancellationToken);
        return new WebV6ImportResult
        {
            NotebookCount = nativeLibrary.Notes.Notebooks.Count,
            SectionCount = nativeLibrary.Notes.Sections.Count,
            PageCount = nativeLibrary.Notes.Pages.Count,
            SheetCount = nativeLibrary.Notes.Sheets.Count,
            DocumentCount = nativeLibrary.Documents.Documents.Count,
            LinkCount = nativeLibrary.Documents.Links.Count,
            WebSheetContentHashes = WebV6Backup.HashesFor(webLibrary),
        };
    }
}
