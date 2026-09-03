using MedNote.Core;

namespace MedNote.Windows.App.ViewModels;

public sealed partial class ReaderViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IPdfEngine _pdfEngine;
    private readonly ReaderPersistenceCoordinator _persistence;
    private readonly BitmapBudget<string> _bitmapBudget = new();
    private readonly PdfRenderScheduler _renderScheduler = new();
    private readonly ReaderSearchCoordinator _search = new();
    private readonly PdfAnnotationSession _annotationSession = new();
    private readonly SemaphoreSlim _documentGate = new(1, 1);
    private IPdfDocumentSession? _session;
    private IReadOnlyList<PdfPageViewModel> _pages = Array.Empty<PdfPageViewModel>();
    private IReadOnlyList<int> _bookmarks = Array.Empty<int>();
    private PdfPageViewModel? _selectionPage;
    private PdfTextSelection? _selectedTextSelection;
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
    private int _rotation;
    private PdfFitMode _fitMode = PdfFitMode.Page;
    private PdfViewMode _viewMode = PdfViewMode.Single;
    private PdfTool _activeTool = PdfTool.Pan;
    private double _viewportWidth = 1_000d;
    private double _viewportHeight = 760d;
    private double _rasterizationScale = 1d;
    private string _inkColor = "#1c2933";
    private string _highlightColor = "#f6d96b";
    private double _inkWidth = 2d;
    private PdfCropResult? _lastCropResult;
    private bool _disposed;

    public ReaderViewModel(IPdfEngine pdfEngine, IReaderLibraryStore libraryStore)
    {
        _pdfEngine = pdfEngine;
        _persistence = new ReaderPersistenceCoordinator(libraryStore);
        _search.PropertyChanged += OnSearchCoordinatorPropertyChanged;
    }
}
