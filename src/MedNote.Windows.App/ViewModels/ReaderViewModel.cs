using MedNote.Core;

namespace MedNote.Windows.App.ViewModels;

public sealed class ReaderViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IPdfEngine _pdfEngine;
    private readonly ReaderPersistenceCoordinator _persistence;
    private readonly BitmapBudget<string> _bitmapBudget = new();
    private readonly PdfRenderScheduler _renderScheduler = new();
    private readonly SemaphoreSlim _documentGate = new(1, 1);
    private IPdfDocumentSession? _session;
    private IReadOnlyList<PdfPageViewModel> _pages = Array.Empty<PdfPageViewModel>();
    private IReadOnlyList<int> _bookmarks = Array.Empty<int>();
    private ReaderState _reader = new();
    private ReaderPosition _position = new();
    private string? _documentId;
    private string? _documentPath;
    private long _documentSize;
    private long _documentLastModified;
    private string _documentName = "MedNote Reader";
    private string _statusText = "Mở một tệp PDF để bắt đầu";
    private bool _isBusy;
    private bool _hasDocument;
    private int _currentPage = 1;
    private int _pageCount;
    private double _zoom = 1d;
    private PdfFitMode _fitMode = PdfFitMode.Page;
    private PdfViewMode _viewMode = PdfViewMode.Single;
    private PdfTool _activeTool = PdfTool.Pan;
    private double _viewportWidth = 1_000d;
    private double _viewportHeight = 760d;
    private double _rasterizationScale = 1d;
    private bool _disposed;

    public ReaderViewModel(IPdfEngine pdfEngine, IReaderLibraryStore libraryStore)
    {
        _pdfEngine = pdfEngine;
        _persistence = new ReaderPersistenceCoordinator(libraryStore);
    }

    public IReadOnlyList<PdfPageViewModel> Pages
    {
        get => _pages;
        private set
        {
            if (SetProperty(ref _pages, value))
            {
                // Opening the first document commonly keeps CurrentPage at 1,
                // so its setter does not fire. The single-page presenter still
                // needs to be told that CurrentPageItem changed from null to
                // the newly-created first page.
                OnPropertyChanged(nameof(CurrentPageItem));
            }
        }
    }

    public IReadOnlyList<int> Bookmarks
    {
        get => _bookmarks;
        private set => SetProperty(ref _bookmarks, value);
    }

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

    public async Task InitializeAsync(bool reopenActiveDocument = true, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            await _persistence.LoadAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            StatusText = $"Không đọc được trạng thái cũ: {exception.Message}";
            return;
        }

        var active = _persistence.ActiveDocument;
        if (!reopenActiveDocument || active is null || !File.Exists(active.Path))
        {
            return;
        }

        try
        {
            await OpenDocumentAsync(active.Path, cancellationToken);
        }
        catch (Exception exception)
        {
            StatusText = $"Không mở lại được tài liệu gần nhất: {exception.Message}";
        }
    }

    public async Task OpenDocumentAsync(string path, CancellationToken cancellationToken = default)
    {
        await _documentGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            IsBusy = true;
            StatusText = "Đang mở PDF…";
            await ReplaceDocumentSessionAsync(path, cancellationToken);
        }
        finally
        {
            IsBusy = false;
            _documentGate.Release();
        }
    }

    public int GoToPage(int requestedPage)
    {
        if (!HasDocument)
        {
            return 1;
        }

        var nextPage = ReaderMath.ClampPage(requestedPage, PageCount);
        CurrentPage = nextPage;
        _reader = _reader with { Page = nextPage };
        _position = _position with { AnchorPage = nextPage };
        QueuePersist();
        return nextPage;
    }

    public void SetZoom(double zoom)
    {
        var normalized = ReaderMath.ClampZoom(zoom);
        if (Math.Abs(normalized - Zoom) < 0.001d)
        {
            return;
        }

        Zoom = normalized;
        _reader = _reader with { Zoom = normalized };
        RefreshAllPageLayouts();
        QueuePersist();
    }

    public void StepZoom(int direction) => SetZoom(ReaderMath.StepZoom(Zoom, direction));

    public void SetFitMode(PdfFitMode mode)
    {
        if (FitMode == mode)
        {
            return;
        }

        FitMode = mode;
        _reader = _reader with { FitMode = mode };
        RefreshAllPageLayouts();
        QueuePersist();
    }

    public void SetViewMode(PdfViewMode mode)
    {
        if (ViewMode == mode)
        {
            return;
        }

        ViewMode = mode;
        _reader = _reader with { ViewMode = mode };
        RefreshAllPageLayouts();
        QueuePersist();
    }

    public void SetActiveTool(PdfTool tool)
    {
        ActiveTool = tool;
    }

    public bool ToggleBookmark()
    {
        if (!HasDocument)
        {
            return false;
        }

        var bookmarks = _reader.Bookmarks.ToHashSet();
        var added = bookmarks.Add(CurrentPage);
        if (!added)
        {
            bookmarks.Remove(CurrentPage);
        }

        var ordered = bookmarks.Order().ToList();
        _reader = _reader with { Bookmarks = ordered };
        Bookmarks = ordered;
        QueuePersist();
        return added;
    }

    public void SetViewport(double width, double height, double rasterizationScale)
    {
        width = Math.Max(320d, width);
        height = Math.Max(320d, height);
        rasterizationScale = Math.Clamp(rasterizationScale, 1d, 3d);
        if (Math.Abs(_viewportWidth - width) < 1d
            && Math.Abs(_viewportHeight - height) < 1d
            && Math.Abs(RasterizationScale - rasterizationScale) < 0.01d)
        {
            return;
        }

        _viewportWidth = width;
        _viewportHeight = height;
        RasterizationScale = rasterizationScale;
        RefreshAllPageLayouts();
    }

    public void RefreshPageLayout(PdfPageViewModel page)
    {
        var layout = CalculatePageLayout(page.AspectRatio);
        page.SetLayout(layout.Width, layout.Height, notify: true);
    }

    private (double Width, double Height) CalculatePageLayout(double aspectRatio)
    {
        var horizontalMargin = ViewMode == PdfViewMode.Continuous ? 56d : 36d;
        var verticalMargin = ViewMode == PdfViewMode.Continuous ? 42d : 36d;
        var availableWidth = Math.Max(280d, _viewportWidth - horizontalMargin);
        var availableHeight = Math.Max(280d, _viewportHeight - verticalMargin);
        var baseWidth = FitMode == PdfFitMode.Width || ViewMode == PdfViewMode.Continuous
            ? availableWidth
            : Math.Min(availableWidth, availableHeight * aspectRatio);
        var width = baseWidth * Zoom;
        return (width, width / Math.Max(0.05d, aspectRatio));
    }

    public void CapturePosition(ReaderPosition position)
    {
        if (!HasDocument)
        {
            return;
        }

        _position = position.Normalize(PageCount);
        if (_position.AnchorPage != CurrentPage)
        {
            CurrentPage = _position.AnchorPage;
            _reader = _reader with { Page = CurrentPage };
        }

        QueuePersist();
    }

    public async ValueTask PersistNowAsync(CancellationToken cancellationToken = default)
    {
        var document = SnapshotDocument();
        if (document is not null)
        {
            await _persistence.SaveNowAsync(document, cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _documentGate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                if (HasDocument)
                {
                    await PersistNowAsync();
                }
            }
            catch
            {
                // State is also persisted after every interaction; shutdown stays unblocked.
            }

            try
            {
                foreach (var page in Pages)
                {
                    page.Dispose();
                }

                _bitmapBudget.Clear();
                if (_session is not null)
                {
                    await _session.DisposeAsync();
                    _session = null;
                }
            }
            finally
            {
                try
                {
                    await _persistence.DisposeAsync();
                }
                finally
                {
                    if (_pdfEngine is IAsyncDisposable disposableEngine)
                    {
                        await disposableEngine.DisposeAsync();
                    }
                }
            }
        }
        finally
        {
            _documentGate.Release();
        }
    }

    private void RefreshAllPageLayouts()
    {
        foreach (var page in Pages)
        {
            var layout = CalculatePageLayout(page.AspectRatio);
            page.SetLayout(
                layout.Width,
                layout.Height,
                notify: page.IsPinned || ReferenceEquals(page, CurrentPageItem));
        }
    }

    private async Task ReplaceDocumentSessionAsync(string path, CancellationToken cancellationToken)
    {
        IPdfDocumentSession? nextSession = null;
        try
        {
            var fullPath = System.IO.Path.GetFullPath(path);
            var info = new FileInfo(fullPath);
            if (!info.Exists)
            {
                throw new FileNotFoundException("Không tìm thấy tệp PDF.", fullPath);
            }

            var lastModified = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds();
            var documentId = DocumentIdentity.Create(info.Name, info.Length, lastModified);
            nextSession = await _pdfEngine.OpenAsync(fullPath, cancellationToken);
            var existing = _persistence.FindDocument(documentId);
            var nextReader = (existing?.Reader ?? new ReaderState()).Normalize(nextSession.PageCount);
            var nextPosition = (existing?.Position ?? new ReaderPosition { AnchorPage = nextReader.Page }).Normalize(nextSession.PageCount);

            var oldSession = _session;
            var oldPages = Pages;
            var openedSession = nextSession;
            _session = openedSession;
            nextSession = null;
            _documentId = documentId;
            _documentPath = fullPath;
            _documentSize = info.Length;
            _documentLastModified = lastModified;
            _reader = nextReader;
            _position = nextPosition;
            DocumentName = info.Name;
            PageCount = openedSession.PageCount;
            CurrentPage = nextReader.Page;
            Zoom = nextReader.Zoom;
            FitMode = nextReader.FitMode;
            ViewMode = nextReader.ViewMode;
            Bookmarks = nextReader.Bookmarks.ToArray();
            Pages = Enumerable.Range(0, PageCount)
                .Select(index =>
                {
                    var metrics = openedSession.PageMetrics[index];
                    var layout = CalculatePageLayout(metrics.AspectRatio);
                    return new PdfPageViewModel(
                        this,
                        openedSession,
                        _bitmapBudget,
                        _renderScheduler,
                        documentId,
                        index,
                        metrics,
                        layout.Width,
                        layout.Height);
                })
                .ToArray();
            HasDocument = true;
            StatusText = $"{PageCount:N0} trang";

            foreach (var page in oldPages)
            {
                page.Dispose();
            }

            if (oldSession is not null)
            {
                await oldSession.DisposeAsync();
            }

            QueuePersist();
        }
        finally
        {
            if (nextSession is not null)
            {
                await nextSession.DisposeAsync();
            }
        }
    }

    private void QueuePersist()
    {
        if (!HasDocument)
        {
            return;
        }

        var document = SnapshotDocument();
        if (document is not null)
        {
            _persistence.QueueSave(
                document,
                exception => StatusText = $"Chưa lưu được vị trí đọc: {exception.Message}");
        }
    }

    private ReaderDocumentRecord? SnapshotDocument()
    {
        if (_documentId is null || _documentPath is null)
        {
            return null;
        }

        return new ReaderDocumentRecord
        {
            Id = _documentId,
            Name = DocumentName,
            Path = _documentPath,
            Size = _documentSize,
            LastModified = _documentLastModified,
            Reader = _reader.Normalize(PageCount),
            Position = _position.Normalize(PageCount),
        };
    }
}
