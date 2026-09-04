using System.Text.Json;
using MedNote.Core;
using MedNote.Core.Compatibility.WebV6;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;

namespace MedNote.Windows.App.Compatibility.WebV6;

/// <summary>
/// UI-thread adapter that lets the Windows Text Object Model emit canonical
/// RichEdit RTF after conservative web-v6 text projection.
/// </summary>
public sealed class WebV6RichEditConverter : IWebV6SheetContentConverter
{
    public ValueTask<RtfSheetContent> ConvertAsync(
        string sheetId,
        JsonElement webContent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var text = WebV6TextProjection.Project(webContent);
        var editor = new RichEditBox();
        editor.Document.SetText(TextSetOptions.None, text);
        editor.Document.Selection.SetRange(0, text.Length);
        var format = editor.Document.Selection.CharacterFormat;
        format.Name = "Segoe UI";
        format.Size = 12f;
        editor.Document.Selection.CharacterFormat = format;
        editor.Document.Selection.SetRange(0, 0);
        editor.Document.GetText(TextGetOptions.FormatRtf, out var rtf);
        var content = new RtfSheetContent { Rtf = rtf };
        NoteLibraryValidator.AssertSheetContentValid(sheetId, content);
        return ValueTask.FromResult(content);
    }
}
