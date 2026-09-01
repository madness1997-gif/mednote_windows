using MedNote.Core;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;

namespace MedNote.Windows.App.Infrastructure;

public sealed class WindowsPdfEngine : IPdfEngine
{
    public async ValueTask<IPdfDocumentSession> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Không tìm thấy tệp PDF.", path);
        }

        var file = await StorageFile.GetFileFromPathAsync(System.IO.Path.GetFullPath(path)).AsTask(cancellationToken);
        var document = await PdfDocument.LoadFromFileAsync(file).AsTask(cancellationToken);
        return new WindowsPdfDocumentSession(path, document);
    }

    private sealed class WindowsPdfDocumentSession(string path, PdfDocument document) : IPdfDocumentSession
    {
        private const uint MaximumRenderEdge = 4_096;
        private const int MaximumConcurrentRenders = 2;
        private readonly PdfDocument _document = document;
        private readonly SemaphoreSlim _renderGate = new(MaximumConcurrentRenders, MaximumConcurrentRenders);
        private int _disposed;

        public string Path { get; } = path;

        public int PageCount => checked((int)_document.PageCount);

        public ValueTask<PdfPageMetrics> GetPageMetricsAsync(int pageIndex, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            ValidatePageIndex(pageIndex);
            using var page = _document.GetPage(checked((uint)pageIndex));
            return ValueTask.FromResult(new PdfPageMetrics(page.Size.Width, page.Size.Height));
        }

        public async ValueTask<RenderedPdfPage> RenderPageAsync(PdfRenderRequest request, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidatePageIndex(request.PageIndex);
            await _renderGate.WaitAsync(cancellationToken);
            try
            {
                ThrowIfDisposed();
                using var page = _document.GetPage(checked((uint)request.PageIndex));
                var naturalRatio = page.Size.Height / Math.Max(1d, page.Size.Width);
                var rawWidth = Math.Max(64d, request.PixelWidth);
                var rawHeight = Math.Max(64d, request.PixelHeight > 0 ? request.PixelHeight : rawWidth * naturalRatio);
                var scale = Math.Min(1d, MaximumRenderEdge / Math.Max(rawWidth, rawHeight));
                var width = checked((uint)Math.Clamp(Math.Round(rawWidth * scale), 64d, MaximumRenderEdge));
                var height = checked((uint)Math.Clamp(Math.Round(rawHeight * scale), 64d, MaximumRenderEdge));
                var options = new PdfPageRenderOptions
                {
                    DestinationWidth = width,
                    DestinationHeight = height,
                    BackgroundColor = Color.FromArgb(255, 255, 255, 255),
                };

                using var output = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(output, options).AsTask(cancellationToken);
                if (output.Size > int.MaxValue)
                {
                    throw new InvalidDataException("Bitmap trang PDF vượt giới hạn bộ nhớ.");
                }

                output.Seek(0);
                using var reader = new DataReader(output.GetInputStreamAt(0));
                await reader.LoadAsync(checked((uint)output.Size)).AsTask(cancellationToken);
                var bytes = new byte[checked((int)output.Size)];
                reader.ReadBytes(bytes);
                return new RenderedPdfPage(bytes, width, height);
            }
            finally
            {
                _renderGate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            // Page controls cancel on unload, but native rendering may already
            // be inside Windows.Data.Pdf. Drain both permits before releasing
            // the session so no render can outlive its document owner.
            for (var index = 0; index < MaximumConcurrentRenders; index++)
            {
                await _renderGate.WaitAsync();
            }

            // Do not dispose a SemaphoreSlim while older callers may still be
            // queued on it. Return the permits so those callers can wake,
            // observe the disposed flag, and exit without touching PDF state.
            _renderGate.Release(MaximumConcurrentRenders);
        }

        private void ValidatePageIndex(int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= PageCount)
            {
                throw new ArgumentOutOfRangeException(nameof(pageIndex));
            }
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
