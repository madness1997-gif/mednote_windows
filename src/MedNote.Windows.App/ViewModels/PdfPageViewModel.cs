using MedNote.Core;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace MedNote.Windows.App.ViewModels;

public sealed class PdfPageViewModel : ObservableObject, IDisposable
{
    private readonly ReaderViewModel _owner;
    private readonly IPdfDocumentSession _session;
    private readonly BitmapBudget<string> _bitmapBudget;
    private readonly string _cacheKey;
    private CancellationTokenSource? _renderCancellation;
    private PdfPageMetrics _metrics = new(707, 1_000);
    private BitmapImage? _bitmap;
    private double _displayWidth = 640;
    private double _displayHeight = 905;
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
        string documentId,
        int pageIndex)
    {
        _owner = owner;
        _session = session;
        _bitmapBudget = bitmapBudget;
        PageIndex = pageIndex;
        Number = pageIndex + 1;
        _cacheKey = $"{documentId}:{Number}";
    }

    public int PageIndex { get; }

    public int Number { get; }

    public string PageLabel => $"Trang {Number}";

    public BitmapImage? Bitmap
    {
        get => _bitmap;
        private set => SetProperty(ref _bitmap, value);
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

    public void SetLayout(double width, double height)
    {
        width = Math.Max(180d, width);
        height = Math.Max(240d, height);
        var widthChanged = Math.Abs(DisplayWidth - width) > 0.5d;
        var heightChanged = Math.Abs(DisplayHeight - height) > 0.5d;
        if (!widthChanged && !heightChanged)
        {
            return;
        }

        DisplayWidth = width;
        DisplayHeight = height;

        var desiredPixelWidth = DesiredPixelSize().Width;
        if (Bitmap is not null && RelativeDifference(_renderedPixelWidth, desiredPixelWidth) > 0.16d)
        {
            EvictBitmap();
        }
    }

    public async Task PinAndRenderAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IsPinned = true;
        await EnsureRenderedAsync(cancellationToken);
    }

    public void Unpin()
    {
        IsPinned = false;
        _renderCancellation?.Cancel();
    }

    public async Task EnsureRenderedAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var desiredPixelSize = DesiredPixelSize();
        var desiredPixelWidth = desiredPixelSize.Width;
        if (Bitmap is not null && RelativeDifference(_renderedPixelWidth, desiredPixelWidth) <= 0.16d)
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
            var metrics = await _session.GetPageMetricsAsync(PageIndex, token);
            if (metrics.Width > 0 && metrics.Height > 0 && metrics != _metrics)
            {
                _metrics = metrics;
                OnPropertyChanged(nameof(AspectRatio));
                _owner.RefreshPageLayout(this);
                desiredPixelSize = DesiredPixelSize();
                desiredPixelWidth = desiredPixelSize.Width;
            }

            var rendered = await _session.RenderPageAsync(
                new PdfRenderRequest(PageIndex, desiredPixelWidth, desiredPixelSize.Height),
                token);
            token.ThrowIfCancellationRequested();

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(rendered.PngBytes);
                await writer.StoreAsync().AsTask(token);
                writer.DetachStream();
            }

            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream).AsTask(token);
            if (generation != _renderGeneration || !IsPinned)
            {
                return;
            }

            Bitmap = bitmap;
            _renderedPixelWidth = rendered.PixelWidth;
            SignalRenderProbe(rendered);
            _bitmapBudget.Report(
                _cacheKey,
                rendered.EstimatedBitmapBytes,
                EvictBitmap,
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
        Bitmap = null;
    }

    private void EvictBitmap()
    {
        _bitmapBudget.Remove(_cacheKey);
        Bitmap = null;
        _renderedPixelWidth = 0;
    }

    private static double RelativeDifference(uint left, uint right) =>
        Math.Abs((double)left - right) / Math.Max(1d, right);

    private void SignalRenderProbe(RenderedPdfPage rendered)
    {
        var probePath = Environment.GetEnvironmentVariable("MEDNOTE_RENDER_PROBE");
        if (string.IsNullOrWhiteSpace(probePath))
        {
            return;
        }

        try
        {
            var directory = System.IO.Path.GetDirectoryName(probePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                probePath,
                $"page={Number};width={rendered.PixelWidth};height={rendered.PixelHeight};bytes={rendered.PngBytes.Length}");
        }
        catch
        {
            // A CI-only render probe must never affect the Reader.
        }
    }

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
