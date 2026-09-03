using MedNote.Core;
using PDFiumCore;

namespace MedNote.Windows.App.Infrastructure;

/// <summary>
/// Serializes document-handle access and exposes renderer-independent Reader
/// capabilities. Native extraction/render implementation details are delegated
/// to focused helpers; PDFium handles never leave this infrastructure layer.
/// </summary>
internal sealed class PdfiumPdfDocumentSession :
    IPdfDocumentSession,
    IPdfOutlineProvider,
    IPdfTextProvider,
    IPdfTextHitTestProvider
{
    private const int MaximumConcurrentOperations = 2;
    private readonly PdfiumDispatcher _dispatcher;
    private readonly FpdfDocumentT _document;
    private readonly IReadOnlyList<PdfPageMetrics> _pageMetrics;
    private readonly SemaphoreSlim _lifetimeGate = new(
        MaximumConcurrentOperations,
        MaximumConcurrentOperations);
    private int _disposed;

    public PdfiumPdfDocumentSession(
        string path,
        FpdfDocumentT document,
        int pageCount,
        IReadOnlyList<PdfPageMetrics> pageMetrics,
        PdfiumDispatcher dispatcher)
    {
        Path = path;
        _document = document;
        PageCount = pageCount;
        _pageMetrics = pageMetrics;
        _dispatcher = dispatcher;
    }

    public string Path { get; }

    public int PageCount { get; }

    public IReadOnlyList<PdfPageMetrics> PageMetrics => _pageMetrics;

    public ValueTask<PdfPageMetrics> GetPageMetricsAsync(
        int pageIndex,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ValidatePageIndex(pageIndex);
        return ValueTask.FromResult(_pageMetrics[pageIndex]);
    }

    public async ValueTask<RenderedPdfPage> RenderPageAsync(
        PdfRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        await _lifetimeGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            ValidatePageIndex(request.PageIndex);
            return await _dispatcher.InvokeAsync(
                () => PdfiumPageRenderer.Render(
                    _document,
                    request.PageIndex,
                    request.PixelWidth,
                    request.PixelHeight,
                    request.Rotation),
                cancellationToken);
        }
        finally
        {
            _lifetimeGate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<PdfOutlineNode>> GetOutlineAsync(
        CancellationToken cancellationToken = default)
    {
        await _lifetimeGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            return await _dispatcher.InvokeAsync<IReadOnlyList<PdfOutlineNode>>(
                () => PdfiumOutlineReader.Read(_document, PageCount),
                cancellationToken);
        }
        finally
        {
            _lifetimeGate.Release();
        }
    }

    public async ValueTask<PdfTextPage> GetTextPageAsync(
        int pageIndex,
        CancellationToken cancellationToken = default)
    {
        await _lifetimeGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            ValidatePageIndex(pageIndex);
            var text = await _dispatcher.InvokeAsync(
                () => PdfiumTextReader.ReadPage(_document, pageIndex),
                cancellationToken);
            return new PdfTextPage(pageIndex, text);
        }
        finally
        {
            _lifetimeGate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<PdfPageRect>> GetTextBoundsAsync(
        int pageIndex,
        int startIndex,
        int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length == 0)
        {
            return Array.Empty<PdfPageRect>();
        }

        _ = checked(startIndex + length);
        await _lifetimeGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            ValidatePageIndex(pageIndex);
            return await _dispatcher.InvokeAsync<IReadOnlyList<PdfPageRect>>(
                () => PdfiumTextReader.ReadBounds(_document, pageIndex, startIndex, length),
                cancellationToken);
        }
        finally
        {
            _lifetimeGate.Release();
        }
    }

    public async ValueTask<int?> GetTextIndexAtPointAsync(
        int pageIndex,
        PdfPagePoint point,
        double horizontalTolerance,
        double verticalTolerance,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(point));
        }

        horizontalTolerance = Math.Max(0d, double.IsFinite(horizontalTolerance) ? horizontalTolerance : 0d);
        verticalTolerance = Math.Max(0d, double.IsFinite(verticalTolerance) ? verticalTolerance : 0d);
        await _lifetimeGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            ValidatePageIndex(pageIndex);
            return await _dispatcher.InvokeAsync<int?>(
                () => PdfiumTextReader.ReadIndexAtPoint(
                    _document,
                    pageIndex,
                    point,
                    horizontalTolerance,
                    verticalTolerance),
                cancellationToken);
        }
        finally
        {
            _lifetimeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        for (var index = 0; index < MaximumConcurrentOperations; index++)
        {
            await _lifetimeGate.WaitAsync();
        }

        try
        {
            await _dispatcher.InvokeAsync(
                () =>
                {
                    fpdfview.FPDF_CloseDocument(_document);
                    return true;
                });
        }
        finally
        {
            // Wake callers that were already queued so they can observe the
            // disposed flag without ever reaching the document handle.
            _lifetimeGate.Release(MaximumConcurrentOperations);
        }
    }

    private void ValidatePageIndex(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
