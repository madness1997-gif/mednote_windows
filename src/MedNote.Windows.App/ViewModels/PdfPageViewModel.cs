using MedNote.Core;

namespace MedNote.Windows.App.ViewModels;

public sealed class PdfPageViewModel : ObservableObject, IDisposable
{
    private readonly ReaderViewModel _owner;
    private readonly IPdfDocumentSession _session;
    private readonly BitmapBudget<string> _bitmapBudget;
    private readonly PdfRenderScheduler _renderScheduler;
    private readonly string _cacheKey;
    private CancellationTokenSource? _renderCancellation;
    private readonly PdfPageMetrics _metrics;
    private RenderedPdfPage? _surface;
    private double _displayWidth;
    private double _displayHeight;
    private bool _isRendering;
    private bool _isPinned;
    private string? _error;
    private uint _renderedPixelWidth;
    private long _renderGeneration;
    private bool _disposed;

    public PdfPageViewModel(
        ReaderViewModel owner,
        IPdfDocumentSession session,
        BitmapBudget<string> bitmapBudget,
        PdfRenderScheduler renderScheduler,
        string documentId,
        int pageIndex,
        PdfPageMetrics metrics,
        double displayWidth,
        double displayHeight)
    {
        _owner = owner;
        _session = session;
        _bitmapBudget = bitmapBudget;
        _renderScheduler = renderScheduler;
        _metrics = metrics;
        _displayWidth = displayWidth;
        _displayHeight = displayHeight;
        PageIndex = pageIndex;
        Number = pageIndex + 1;
        _cacheKey = $"{documentId}:{Number}";
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

    public double DisplayWidth
    {
        get => _displayWidth;
        private set => SetProperty(ref _displayWidth, value);
    }

    public double DisplayHeight
    {
        get => _displayHeight;
        private set => SetProperty(ref _displayHeight, value);
    }

    public bool IsRendering
    {
        get => _isRendering;
        private set => SetProperty(ref _isRendering, value);
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

    public double AspectRatio => _metrics.AspectRatio;

    public void SetLayout(double width, double height, bool notify)
    {
        width = Math.Max(180d, width);
        height = Math.Max(240d, height);
        var widthChanged = Math.Abs(DisplayWidth - width) > 0.5d;
        var heightChanged = Math.Abs(DisplayHeight - height) > 0.5d;
        if (!widthChanged && !heightChanged)
        {
            return;
        }

        if (notify)
        {
            DisplayWidth = width;
            DisplayHeight = height;
        }
        else
        {
            _displayWidth = width;
            _displayHeight = height;
        }

        var desiredPixelWidth = DesiredPixelSize().Width;
        if (Surface is not null && RelativeDifference(_renderedPixelWidth, desiredPixelWidth) > 0.16d)
        {
            EvictSurface();
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

    internal void ReportPresentationError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Error = $"Không hiển thị được trang {Number} bằng Direct2D: {exception.Message}";
    }

    internal void ReportPresentationSucceeded() => Error = null;

    public async Task EnsureRenderedAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var desiredPixelSize = DesiredPixelSize();
        var desiredPixelWidth = desiredPixelSize.Width;
        if (Surface is not null && RelativeDifference(_renderedPixelWidth, desiredPixelWidth) <= 0.16d)
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
                    new PdfRenderRequest(PageIndex, desiredPixelWidth, desiredPixelSize.Height),
                    renderToken),
                token);
            token.ThrowIfCancellationRequested();
            if (generation != _renderGeneration || !IsPinned)
            {
                return;
            }

            if (!rendered.HasValidBuffer)
            {
                throw new InvalidDataException($"Buffer BGRA của trang {Number} không hợp lệ.");
            }

            Surface = rendered;
            _renderedPixelWidth = rendered.PixelWidth;
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _renderCancellation?.Cancel();
        _renderCancellation?.Dispose();
        _bitmapBudget.Remove(_cacheKey);
        Surface = null;
    }

    private void EvictSurface()
    {
        _bitmapBudget.Remove(_cacheKey);
        Surface = null;
        _renderedPixelWidth = 0;
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
}
