using System.ComponentModel;
using MedNote.Core;

namespace MedNote.Windows.App.ViewModels;

public sealed partial class ReaderViewModel
{
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
