using MedNote.Core;

namespace MedNote.Windows.App.ViewModels;

public sealed class PdfPageViewModel : ObservableObject, IDisposable
{
    private readonly ReaderViewModel _owner;
    private readonly IPdfDocumentSession _session;
    private readonly BitmapBudget<string> _bitmapBudget;
    private readonly PdfRenderScheduler _renderScheduler;
    private readonly string _cacheKey;
    private readonly string _thumbnailCacheKey;
    private CancellationTokenSource? _renderCancellation;
    private CancellationTokenSource? _thumbnailRenderCancellation;
    private readonly PdfPageMetrics _metrics;
    private RenderedPdfPage? _surface;
    private RenderedPdfPage? _thumbnailSurface;
    private PdfTextPage? _textPage;
    private PdfTextSelection? _selection;
    private double _displayWidth;
    private double _displayHeight;
    private int _rotation;
    private bool _isRendering;
    private bool _isThumbnailRendering;
    private bool _isPinned;
    private bool _isThumbnailPinned;
    private string? _error;
    private uint _renderedPixelWidth;
    private int _renderedRotation = -1;
    private long _renderGeneration;
    private long _thumbnailRenderGeneration;
    private bool _disposed;

    public PdfPageViewModel(
        ReaderViewModel owner,
        IPdfDocumentSession session,
        BitmapBudget<string> bitmapBudget,
        PdfRenderScheduler renderScheduler,
        string documentId,
        int pageIndex,
        PdfPageMetrics metrics,
        int rotation,
        double displayWidth,
        double displayHeight)
    {
        _owner = owner;
        _session = session;
        _bitmapBudget = bitmapBudget;
        _renderScheduler = renderScheduler;
        _metrics = metrics;
        _rotation = ReaderMath.NormalizeRotation(rotation);
        _displayWidth = displayWidth;
        _displayHeight = displayHeight;
        PageIndex = pageIndex;
        Number = pageIndex + 1;
        _cacheKey = $"{documentId}:{Number}";
        _thumbnailCacheKey = $"{documentId}:thumbnail:{Number}";
    }

    public int PageIndex { get; }

    public int Number { get; }

    public string PageLabel => $"Trang {Number}";

    internal int OwnerPageCount => _owner.PageCount;

    public RenderedPdfPage? Surface
    {
        get => _surface;
        private set => SetProperty(ref _surface, value);
    }

    public RenderedPdfPage? ThumbnailSurface
    {
        get => _thumbnailSurface;
        private set => SetProperty(ref _thumbnailSurface, value);
    }

    public PdfTextSelection? Selection
    {
        get => _selection;
        private set => SetProperty(ref _selection, value);
    }

    public IReadOnlyList<PdfAnnotation> Annotations => _owner.GetAnnotationsForPage(Number);

    public double DisplayWidth => _displayWidth;

    public double DisplayHeight => _displayHeight;

    public int Rotation => _rotation;

    public bool IsRendering
    {
        get => _isRendering;
        private set => SetProperty(ref _isRendering, value);
    }

    public bool IsThumbnailRendering
    {
        get => _isThumbnailRendering;
        private set => SetProperty(ref _isThumbnailRendering, value);
    }

    public bool IsPinned
    {
        get => _isPinned;
        private set => SetProperty(ref _isPinned, value);
    }

    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(ErrorOpacity));
            }
        }
    }

    public double ErrorOpacity => string.IsNullOrWhiteSpace(Error) ? 0d : 1d;

    public double AspectRatio => _metrics.AspectRatioForRotation(Rotation);

    internal double AspectRatioForRotation(int rotation) => _metrics.AspectRatioForRotation(rotation);

    public void SetLayout(double width, double height, bool notify) =>
        SetLayout(width, height, Rotation, notify);

    internal void SetLayout(double width, double height, int rotation, bool notify)
    {
        width = Math.Max(180d, width);
        height = Math.Max(240d, height);
        rotation = ReaderMath.NormalizeRotation(rotation);
        var widthChanged = Math.Abs(_displayWidth - width) > 0.5d;
        var heightChanged = Math.Abs(_displayHeight - height) > 0.5d;
        var rotationChanged = _rotation != rotation;
        if (!widthChanged && !heightChanged && !rotationChanged)
        {
            return;
        }

        if (rotationChanged)
        {
            _renderCancellation?.Cancel();
            _thumbnailRenderCancellation?.Cancel();
            _renderGeneration++;
            _thumbnailRenderGeneration++;
            IsRendering = false;
            IsThumbnailRendering = false;
            Error = null;
        }

        _displayWidth = width;
        _displayHeight = height;
        _rotation = rotation;

        var desiredPixelWidth = DesiredPixelSize().Width;
        if (Surface is not null
            && (rotationChanged || RelativeDifference(_renderedPixelWidth, desiredPixelWidth) > 0.16d))
        {
            EvictSurface();
        }


        if (rotationChanged)
        {
            EvictThumbnailSurface();
        }

        if (!notify)
        {
            return;
        }

        if (widthChanged)
        {
            OnPropertyChanged(nameof(DisplayWidth));
        }

        if (heightChanged)
        {
            OnPropertyChanged(nameof(DisplayHeight));
        }

        if (rotationChanged)
        {
            OnPropertyChanged(nameof(Rotation));
            OnPropertyChanged(nameof(AspectRatio));
        }
    }

    public async Task PinAndRenderAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IsPinned = true;
        _owner.RefreshPageLayout(this);
        await EnsureRenderedAsync(cancellationToken);
    }

    public void Unpin()
    {
        IsPinned = false;
        _renderCancellation?.Cancel();
    }

    public async Task PinAndRenderThumbnailAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _isThumbnailPinned = true;
        await EnsureThumbnailRenderedAsync(cancellationToken);
    }

    public void UnpinThumbnail()
    {
        _isThumbnailPinned = false;
        _thumbnailRenderCancellation?.Cancel();
    }

    public async ValueTask<int?> GetTextIndexAtDisplayPointAsync(
        PdfPagePoint displayPoint,
        double tolerancePixels = 5d,
        CancellationToken cancellationToken = default)
    {
        if (_session is not IPdfTextHitTestProvider hitTestProvider)
        {
            return null;
        }

        var point = PdfPageCoordinateMapper.DisplayToPage(
            displayPoint,
            _metrics,
            DisplayWidth,
            DisplayHeight,
            Rotation);
        var offsetX = PdfPageCoordinateMapper.DisplayToPage(
            new PdfPagePoint(displayPoint.X + tolerancePixels, displayPoint.Y),
            _metrics,
            DisplayWidth,
            DisplayHeight,
            Rotation);
        var offsetY = PdfPageCoordinateMapper.DisplayToPage(
            new PdfPagePoint(displayPoint.X, displayPoint.Y + tolerancePixels),
            _metrics,
            DisplayWidth,
            DisplayHeight,
            Rotation);
        var horizontalTolerance = Math.Max(Math.Abs(offsetX.X - point.X), Math.Abs(offsetY.X - point.X));
        var verticalTolerance = Math.Max(Math.Abs(offsetX.Y - point.Y), Math.Abs(offsetY.Y - point.Y));
        return await hitTestProvider.GetTextIndexAtPointAsync(
            PageIndex,
            point,
            horizontalTolerance,
            verticalTolerance,
            cancellationToken);
    }

    public Task<PdfTextSelection?> SelectTextBetweenAsync(
        int anchorIndex,
        int focusIndex,
        CancellationToken cancellationToken = default)
    {
        var startIndex = Math.Min(anchorIndex, focusIndex);
        var length = checked(Math.Abs(focusIndex - anchorIndex) + 1);
        return SelectTextRangeAsync(startIndex, length, cancellationToken);
    }

    public void ClearTextSelection() => _owner.SetTextSelection(this, null);

    public async Task<PdfTextSelection?> SelectTextRangeAsync(
        int startIndex,
        int length,
        CancellationToken cancellationToken = default)
    {
        if (_session is not IPdfTextProvider textProvider || length <= 0)
        {
            _owner.SetTextSelection(this, null);
            return null;
        }

        var textPage = _textPage ??= await textProvider.GetTextPageAsync(PageIndex, cancellationToken);
        if (textPage.Text.Length == 0 || startIndex >= textPage.Text.Length)
        {
            _owner.SetTextSelection(this, null);
            return null;
        }

        startIndex = Math.Clamp(startIndex, 0, textPage.Text.Length - 1);
        length = Math.Clamp(length, 1, textPage.Text.Length - startIndex);
        var bounds = await textProvider.GetTextBoundsAsync(PageIndex, startIndex, length, cancellationToken);
        var selection = new PdfTextSelection(
            PageIndex,
            startIndex,
            length,
            textPage.Text.Substring(startIndex, length),
            bounds);
        _owner.SetTextSelection(this, selection);
        return selection;
    }

    public IReadOnlyList<PdfPageRect> GetDisplaySelectionBounds()
    {
        if (Selection is not { } selection)
        {
            return Array.Empty<PdfPageRect>();
        }

        return selection.Bounds
            .Select(rectangle => PdfPageCoordinateMapper.PageToDisplay(
                rectangle,
                _metrics,
                DisplayWidth,
                DisplayHeight,
                Rotation))
            .Where(rectangle => rectangle.Width > 0d && rectangle.Height > 0d)
            .ToArray();
    }

    internal bool IsTextSelectionEnabled => _owner.ActiveTool is PdfTool.Select
        or PdfTool.Highlight
        or PdfTool.Underline
        or PdfTool.Strikeout
        or PdfTool.Squiggly;

    internal PdfTool ActiveTool => _owner.ActiveTool;

    internal string InkColor => _owner.InkColor;

    internal string HighlightColor => _owner.HighlightColor;

    internal double InkWidth => _owner.InkWidth;

    internal PdfAnnotationPoint DisplayPointToAnnotation(PdfPagePoint point, double pressure = 0.5d) =>
        PdfAnnotationCoordinateMapper.DisplayToAnnotation(
            point,
            _metrics,
            DisplayWidth,
            DisplayHeight,
            Rotation,
            pressure);

    internal PdfAnnotationRect DisplayRectToAnnotation(PdfPagePoint first, PdfPagePoint second) =>
        PdfAnnotationCoordinateMapper.DisplayToAnnotation(
            first,
            second,
            _metrics,
            DisplayWidth,
            DisplayHeight,
            Rotation);

    internal PdfPageRect AnnotationRectToDisplay(PdfAnnotationRect rectangle) =>
        PdfAnnotationCoordinateMapper.AnnotationToDisplay(
            rectangle,
            _metrics,
            DisplayWidth,
            DisplayHeight,
            Rotation);

    internal PdfPagePoint AnnotationPointToDisplay(PdfAnnotationPoint point) =>
        PdfAnnotationCoordinateMapper.AnnotationToDisplay(
            point,
            _metrics,
            DisplayWidth,
            DisplayHeight,
            Rotation);

    internal PdfAnnotationRect PageRectToAnnotation(PdfPageRect rectangle) =>
        new(
            rectangle.Left,
            _metrics.Height - rectangle.Bottom,
            rectangle.Right,
            _metrics.Height - rectangle.Top);

    internal double DisplayStrokeWidthToPage(double width)
    {
        var start = DisplayPointToAnnotation(new PdfPagePoint(0d, 0d));
        var end = DisplayPointToAnnotation(new PdfPagePoint(width, 0d));
        var result = Math.Sqrt(Math.Pow(end.X - start.X, 2d) + Math.Pow(end.Y - start.Y, 2d));
        return Math.Max(0.1d, result);
    }

    internal double PageStrokeWidthToDisplay(double width)
    {
        var start = AnnotationPointToDisplay(new PdfAnnotationPoint(0d, 0d));
        var end = AnnotationPointToDisplay(new PdfAnnotationPoint(width, 0d));
        var result = Math.Sqrt(Math.Pow(end.X - start.X, 2d) + Math.Pow(end.Y - start.Y, 2d));
        return Math.Max(1d, result);
    }

    internal bool AddAnnotation(PdfAnnotation annotation) => _owner.AddAnnotation(annotation);

    internal bool DeleteAnnotations(IEnumerable<string> annotationIds) =>
        _owner.DeleteAnnotations(annotationIds);

    internal bool AddSelectionMarkup(PdfAnnotationKind kind) => _owner.AddSelectionMarkup(this, kind);

    internal bool CommitSelectionMarkupForActiveTool() => _owner.CommitSelectionMarkupForActiveTool(this);

    internal ValueTask<PdfCropResult?> CreateCropAsync(
        PdfAnnotationRect rectangle,
        CancellationToken cancellationToken = default) =>
        _owner.CreateCropAsync(this, rectangle, cancellationToken);

    internal void NotifyAnnotationsChanged() => OnPropertyChanged(nameof(Annotations));

    internal void SetSelectionFromOwner(PdfTextSelection? selection) => Selection = selection;

    internal void ReportPresentationError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Error = $"Không hiển thị được trang {Number} bằng Direct2D: {exception.Message}";
    }

    internal void ReportPresentationSucceeded() => Error = null;

    internal void ReportInteractionError(string message) => Error = message;

    public async Task EnsureRenderedAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var rotation = Rotation;
        var desiredPixelSize = DesiredPixelSize();
        var desiredPixelWidth = desiredPixelSize.Width;
        if (Surface is not null
            && _renderedRotation == rotation
            && RelativeDifference(_renderedPixelWidth, desiredPixelWidth) <= 0.16d)
        {
            _bitmapBudget.Touch(_cacheKey);
            return;
        }

        _renderCancellation?.Cancel();
        _renderCancellation?.Dispose();
        _renderCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _renderCancellation.Token;
        var generation = ++_renderGeneration;
        IsRendering = true;
        Error = null;

        try
        {
            var estimatedBytes = checked((long)desiredPixelSize.Width * desiredPixelSize.Height * 4L);
            var rendered = await _renderScheduler.RunAsync(
                estimatedBytes,
                renderToken => _session.RenderPageAsync(
                    new PdfRenderRequest(
                        PageIndex,
                        desiredPixelWidth,
                        desiredPixelSize.Height,
                        rotation),
                    renderToken),
                token);
            token.ThrowIfCancellationRequested();
            if (generation != _renderGeneration || rotation != Rotation || !IsPinned)
            {
                return;
            }

            if (!rendered.HasValidBuffer)
            {
                throw new InvalidDataException($"Buffer BGRA của trang {Number} không hợp lệ.");
            }

            _renderedPixelWidth = rendered.PixelWidth;
            _renderedRotation = rotation;
            Surface = rendered;
            _bitmapBudget.Report(
                _cacheKey,
                rendered.EstimatedResidentBytes,
                EvictSurface,
                () => IsPinned);
        }
        catch (OperationCanceledException)
        {
            // Virtualization intentionally cancels pages that leave the viewport.
        }
        catch (Exception exception)
        {
            if (generation == _renderGeneration)
            {
                Error = $"Không dựng được trang {Number}: {exception.Message}";
            }
        }
        finally
        {
            if (generation == _renderGeneration)
            {
                IsRendering = false;
            }
        }
    }

    private async Task EnsureThumbnailRenderedAsync(CancellationToken cancellationToken)
    {
        if (ThumbnailSurface is not null)
        {
            _bitmapBudget.Touch(_thumbnailCacheKey);
            return;
        }

        _thumbnailRenderCancellation?.Cancel();
        _thumbnailRenderCancellation?.Dispose();
        _thumbnailRenderCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _thumbnailRenderCancellation.Token;
        var generation = ++_thumbnailRenderGeneration;
        var size = DesiredThumbnailPixelSize();
        IsThumbnailRendering = true;
        try
        {
            var estimatedBytes = checked((long)size.Width * size.Height * 4L);
            var rendered = await _renderScheduler.RunAsync(
                estimatedBytes,
                renderToken => _session.RenderPageAsync(
                    new PdfRenderRequest(PageIndex, size.Width, size.Height, Rotation),
                    renderToken),
                token);
            token.ThrowIfCancellationRequested();
            if (generation != _thumbnailRenderGeneration || !_isThumbnailPinned)
            {
                return;
            }

            if (!rendered.HasValidBuffer)
            {
                throw new InvalidDataException($"Thumbnail trang {Number} không hợp lệ.");
            }

            ThumbnailSurface = rendered;
            _bitmapBudget.Report(
                _thumbnailCacheKey,
                rendered.EstimatedResidentBytes,
                EvictThumbnailSurface,
                () => _isThumbnailPinned);
        }
        catch (OperationCanceledException)
        {
            // A recycled sidebar item no longer needs its thumbnail.
        }
        finally
        {
            if (generation == _thumbnailRenderGeneration)
            {
                IsThumbnailRendering = false;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _renderCancellation?.Cancel();
        _renderCancellation?.Dispose();
        _thumbnailRenderCancellation?.Cancel();
        _thumbnailRenderCancellation?.Dispose();
        _bitmapBudget.Remove(_cacheKey);
        _bitmapBudget.Remove(_thumbnailCacheKey);
        Surface = null;
        ThumbnailSurface = null;
        Selection = null;
    }

    private void EvictSurface()
    {
        _bitmapBudget.Remove(_cacheKey);
        Surface = null;
        _renderedPixelWidth = 0;
        _renderedRotation = -1;
    }

    private void EvictThumbnailSurface()
    {
        _bitmapBudget.Remove(_thumbnailCacheKey);
        ThumbnailSurface = null;
    }

    private static double RelativeDifference(uint left, uint right) =>
        Math.Abs((double)left - right) / Math.Max(1d, right);

    private (uint Width, uint Height) DesiredPixelSize()
    {
        const double maximumEdge = 4_096d;
        var rawWidth = Math.Max(64d, DisplayWidth * _owner.RasterizationScale);
        var rawHeight = Math.Max(64d, rawWidth / Math.Max(0.05d, AspectRatio));
        var scale = Math.Min(1d, maximumEdge / Math.Max(rawWidth, rawHeight));
        return (
            checked((uint)Math.Clamp(Math.Round(rawWidth * scale), 64d, maximumEdge)),
            checked((uint)Math.Clamp(Math.Round(rawHeight * scale), 64d, maximumEdge)));
    }

    private (uint Width, uint Height) DesiredThumbnailPixelSize()
    {
        const double maximumEdge = 160d;
        var rawWidth = 112d;
        var rawHeight = rawWidth / Math.Max(0.05d, AspectRatio);
        var scale = Math.Min(1d, maximumEdge / Math.Max(rawWidth, rawHeight));
        return (
            checked((uint)Math.Clamp(Math.Round(rawWidth * scale), 32d, maximumEdge)),
            checked((uint)Math.Clamp(Math.Round(rawHeight * scale), 32d, maximumEdge)));
    }
}
