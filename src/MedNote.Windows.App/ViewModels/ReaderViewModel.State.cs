using System.Collections.ObjectModel;
using MedNote.Core;

namespace MedNote.Windows.App.ViewModels;

public sealed partial class ReaderViewModel
{
    public IReadOnlyList<PdfPageViewModel> Pages
    {
        get => _pages;
        private set
        {
            if (SetProperty(ref _pages, value))
            {
                // Opening the first document commonly keeps CurrentPage at 1,
                // so its setter does not fire. The single-page presenter still
                // needs the CurrentPageItem notification.
                OnPropertyChanged(nameof(CurrentPageItem));
            }
        }
    }

    public IReadOnlyList<int> Bookmarks
    {
        get => _bookmarks;
        private set => SetProperty(ref _bookmarks, value);
    }

    public ObservableCollection<PdfSearchMatch> SearchResults => _search.Results;

    public string SearchStatus => _search.Status;

    public bool IsSearching => _search.IsSearching;

    public PdfTextSelection? SelectedTextSelection
    {
        get => _selectedTextSelection;
        private set
        {
            if (SetProperty(ref _selectedTextSelection, value))
            {
                OnPropertyChanged(nameof(HasTextSelection));
                OnPropertyChanged(nameof(SelectedText));
            }
        }
    }

    public bool HasTextSelection => SelectedTextSelection is { Length: > 0 };

    public string SelectedText => SelectedTextSelection?.Text ?? string.Empty;

    public string DocumentName
    {
        get => _documentName;
        private set => SetProperty(ref _documentName, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool HasDocument
    {
        get => _hasDocument;
        private set => SetProperty(ref _hasDocument, value);
    }

    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(CurrentPageItem));
                OnPropertyChanged(nameof(PageSummary));
            }
        }
    }

    public int PageCount
    {
        get => _pageCount;
        private set
        {
            if (SetProperty(ref _pageCount, value))
            {
                OnPropertyChanged(nameof(PageSummary));
            }
        }
    }

    public string PageSummary => PageCount == 0 ? "—" : $"{CurrentPage} / {PageCount}";

    public PdfPageViewModel? CurrentPageItem =>
        CurrentPage >= 1 && CurrentPage <= Pages.Count ? Pages[CurrentPage - 1] : null;

    public double Zoom
    {
        get => _zoom;
        private set
        {
            if (SetProperty(ref _zoom, value))
            {
                OnPropertyChanged(nameof(ZoomLabel));
            }
        }
    }

    public string ZoomLabel => $"{Zoom:P0}";

    public int Rotation
    {
        get => _rotation;
        private set => SetProperty(ref _rotation, value);
    }

    public PdfFitMode FitMode
    {
        get => _fitMode;
        private set => SetProperty(ref _fitMode, value);
    }

    public PdfViewMode ViewMode
    {
        get => _viewMode;
        private set => SetProperty(ref _viewMode, value);
    }

    public PdfTool ActiveTool
    {
        get => _activeTool;
        private set => SetProperty(ref _activeTool, value);
    }

    public double RasterizationScale
    {
        get => _rasterizationScale;
        private set => SetProperty(ref _rasterizationScale, value);
    }

    public ReaderPosition SavedPosition => _position;

    public string? PendingPasswordDocumentPath { get; private set; }
}
