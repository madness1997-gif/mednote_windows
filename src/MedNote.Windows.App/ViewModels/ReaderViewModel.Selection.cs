using System.ComponentModel;
using MedNote.Core;

namespace MedNote.Windows.App.ViewModels;

public sealed partial class ReaderViewModel
{
    public event Action<PdfTextExcerpt>? TextExcerptRequested;

    internal void SendSelectionToNote(PdfPageViewModel page, string? text)
    {
        if (DocumentId is not { } id || page.Selection is not { } selection
            || !ReferenceEquals(_selectionPage, page) || selection.Bounds.Count == 0)
        {
            return;
        }

        var bounds = selection.Bounds.Select(page.PageRectToAnnotation).ToArray();
        var rect = new PdfAnnotationRect(bounds.Min(b => b.Left), bounds.Min(b => b.Bottom),
            bounds.Max(b => b.Right), bounds.Max(b => b.Top));
        var content = text ?? selection.Text;
        if (!string.IsNullOrWhiteSpace(content))
        {
            TextExcerptRequested?.Invoke(new PdfTextExcerpt(id, DocumentName, page.Number, rect, content));
        }
    }

    public Task SearchAsync(string query) => _search.SearchAsync(query);

    public void ClearTextSelection()
    {
        _selectionPage?.SetSelectionFromOwner(null);
        _selectionPage = null;
        SelectedTextSelection = null;
    }

    internal void SetTextSelection(PdfPageViewModel page, PdfTextSelection? selection)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!ReferenceEquals(_selectionPage, page))
        {
            _selectionPage?.SetSelectionFromOwner(null);
        }

        _selectionPage = selection is null ? null : page;
        page.SetSelectionFromOwner(selection);
        SelectedTextSelection = selection;
    }

    private void OnSearchCoordinatorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ReaderSearchCoordinator.Status):
                OnPropertyChanged(nameof(SearchStatus));
                break;
            case nameof(ReaderSearchCoordinator.IsSearching):
                OnPropertyChanged(nameof(IsSearching));
                break;
        }
    }
}
