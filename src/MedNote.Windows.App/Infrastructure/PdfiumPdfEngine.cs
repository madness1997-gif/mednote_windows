using System.Runtime.InteropServices;
using System.Text;
using MedNote.Core;
using PDFiumCore;

namespace MedNote.Windows.App.Infrastructure;

public sealed class PdfiumPdfEngine : IPdfEngine, IAsyncDisposable
{
    private readonly PdfiumDispatcher _dispatcher = new();
    private int _disposed;

    public async ValueTask<IPdfDocumentSession> OpenAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Không tìm thấy tệp PDF.", fullPath);
        }

        var opened = await _dispatcher.InvokeAsync(
            () =>
            {
                var document = fpdfview.FPDF_LoadDocument(fullPath, null);
                if (document is null)
                {
                    throw CreateLoadException(fullPath, fpdfview.FPDF_GetLastError());
                }

                var pageCount = fpdfview.FPDF_GetPageCount(document);
                if (pageCount <= 0)
                {
                    fpdfview.FPDF_CloseDocument(document);
                    throw new InvalidDataException("PDF không có trang hợp lệ.");
                }

                try
                {
                    return (
                        Document: document,
                        PageCount: pageCount,
                        PageMetrics: ReadAllPageMetrics(document, pageCount, cancellationToken));
                }
                catch
                {
                    fpdfview.FPDF_CloseDocument(document);
                    throw;
                }
            },
            cancellationToken);

        return new PdfiumPdfDocumentSession(
            fullPath,
            opened.Document,
            opened.PageCount,
            opened.PageMetrics,
            _dispatcher);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _dispatcher.DisposeAsync();
    }

    private static Exception CreateLoadException(string path, ulong errorCode) => errorCode switch
    {
        2 => new IOException($"Không thể đọc tệp PDF: {path}"),
        3 => new InvalidDataException("Tệp không đúng định dạng PDF hoặc đã bị hỏng."),
        4 => new UnauthorizedAccessException("PDF được bảo vệ bằng mật khẩu; bản hiện tại chưa có hộp nhập mật khẩu."),
        5 => new UnauthorizedAccessException("Thiết lập bảo mật của PDF không cho phép mở tài liệu."),
        6 => new InvalidDataException("PDF chứa lỗi cấu trúc trang."),
        _ => new InvalidDataException($"PDFium không mở được tài liệu (mã lỗi {errorCode})."),
    };

    private static IReadOnlyList<PdfPageMetrics> ReadAllPageMetrics(
        FpdfDocumentT document,
        int pageCount,
        CancellationToken cancellationToken)
    {
        var metrics = new PdfPageMetrics[pageCount];
        using var size = new FS_SIZEF_();
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            if ((pageIndex & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            size.Width = 0;
            size.Height = 0;
            var succeeded = fpdfview.FPDF_GetPageSizeByIndexF(document, pageIndex, size) != 0;
            metrics[pageIndex] = succeeded && size.Width > 0 && size.Height > 0
                ? new PdfPageMetrics(size.Width, size.Height)
                : new PdfPageMetrics(707d, 1_000d);
        }

        return metrics;
    }

    private sealed class PdfiumPdfDocumentSession : IPdfDocumentSession, IPdfOutlineProvider, IPdfTextProvider
    {
        private const int MaximumRenderEdge = 4_096;
        private const int MaximumConcurrentOperations = 2;
        private const int MaximumOutlineDepth = 64;
        private const int MaximumOutlineNodes = 10_000;
        private const ulong MaximumOutlineTitleBytes = 1_048_576;
        private const int MaximumTextCharactersPerPage = 8_000_000;
        private const int MaximumTextRectanglesPerRequest = 100_000;
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
                    () => RenderPageToBgra(request.PageIndex, request.PixelWidth, request.PixelHeight),
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
                    ReadOutline,
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
                    () => ReadTextPage(pageIndex),
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
                    () => ReadTextBounds(pageIndex, startIndex, length),
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
                // Wake callers that were already queued so they can observe
                // the disposed flag without ever reaching the document handle.
                _lifetimeGate.Release(MaximumConcurrentOperations);
            }
        }

        private RenderedPdfPage RenderPageToBgra(int pageIndex, uint requestedWidth, uint requestedHeight)
        {
            var page = LoadPage(pageIndex);
            FpdfBitmapT? bitmap = null;
            try
            {
                var pageWidth = Math.Max(1d, fpdfview.FPDF_GetPageWidthF(page));
                var pageHeight = Math.Max(1d, fpdfview.FPDF_GetPageHeightF(page));
                var rawWidth = Math.Max(64d, requestedWidth);
                var rawHeight = Math.Max(64d, requestedHeight > 0 ? requestedHeight : rawWidth * pageHeight / pageWidth);
                var scale = Math.Min(1d, MaximumRenderEdge / Math.Max(rawWidth, rawHeight));
                var width = checked((int)Math.Clamp(Math.Round(rawWidth * scale), 64d, MaximumRenderEdge));
                var height = checked((int)Math.Clamp(Math.Round(rawHeight * scale), 64d, MaximumRenderEdge));
                bitmap = fpdfview.FPDFBitmapCreateEx(
                    width,
                    height,
                    (int)FPDFBitmapFormat.BGRA,
                    IntPtr.Zero,
                    0);
                if (bitmap is null)
                {
                    throw new OutOfMemoryException("PDFium không tạo được bitmap trang.");
                }

                fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, width, height, uint.MaxValue);
                var flags = RenderFlags.RenderAnnotations
                    | RenderFlags.OptimizeTextForLcd
                    | RenderFlags.LimitImageCacheSize;
                fpdfview.FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, (int)flags);

                var buffer = fpdfview.FPDFBitmapGetBuffer(bitmap);
                var stride = fpdfview.FPDFBitmapGetStride(bitmap);
                var rowBytes = checked(width * 4);
                if (buffer == IntPtr.Zero || stride < rowBytes)
                {
                    throw new InvalidDataException("PDFium trả về bitmap không hợp lệ.");
                }

                var pixels = new byte[checked(rowBytes * height)];
                for (var row = 0; row < height; row++)
                {
                    Marshal.Copy(
                        IntPtr.Add(buffer, checked(row * stride)),
                        pixels,
                        checked(row * rowBytes),
                        rowBytes);
                }

                return new RenderedPdfPage(
                    pixels,
                    checked((uint)width),
                    checked((uint)height),
                    checked((uint)rowBytes));
            }
            finally
            {
                if (bitmap is not null)
                {
                    fpdfview.FPDFBitmapDestroy(bitmap);
                }

                fpdfview.FPDF_ClosePage(page);
            }
        }

        private IReadOnlyList<PdfOutlineNode> ReadOutline()
        {
            var visited = new HashSet<IntPtr>();
            var nodeCount = 0;
            return ReadOutlineLevel(null, 0, visited, ref nodeCount);
        }

        private IReadOnlyList<PdfOutlineNode> ReadOutlineLevel(
            FpdfBookmarkT? parent,
            int depth,
            HashSet<IntPtr> visited,
            ref int nodeCount)
        {
            if (depth >= MaximumOutlineDepth || nodeCount >= MaximumOutlineNodes)
            {
                return Array.Empty<PdfOutlineNode>();
            }

            var nodes = new List<PdfOutlineNode>();
            var bookmark = fpdf_doc.FPDFBookmarkGetFirstChild(_document, parent!);
            while (bookmark is not null
                && nodeCount < MaximumOutlineNodes
                && visited.Add(bookmark.__Instance))
            {
                nodeCount++;
                var children = ReadOutlineLevel(bookmark, depth + 1, visited, ref nodeCount);
                nodes.Add(new PdfOutlineNode(
                    ReadBookmarkTitle(bookmark),
                    ResolveDestination(bookmark),
                    children,
                    fpdf_doc.FPDFBookmarkGetCount(bookmark) > 0));
                bookmark = fpdf_doc.FPDFBookmarkGetNextSibling(_document, bookmark);
            }

            return nodes;
        }

        private static string ReadBookmarkTitle(FpdfBookmarkT bookmark)
        {
            var requiredBytes = fpdf_doc.FPDFBookmarkGetTitle(bookmark, IntPtr.Zero, 0);
            if (requiredBytes < 2 || requiredBytes > MaximumOutlineTitleBytes)
            {
                return string.Empty;
            }

            var buffer = Marshal.AllocHGlobal(checked((int)requiredBytes));
            try
            {
                var writtenBytes = Math.Min(
                    requiredBytes,
                    fpdf_doc.FPDFBookmarkGetTitle(bookmark, buffer, requiredBytes));
                var bytes = new byte[checked((int)writtenBytes)];
                Marshal.Copy(buffer, bytes, 0, bytes.Length);
                var textLength = bytes.Length;
                while (textLength >= 2 && bytes[textLength - 1] == 0 && bytes[textLength - 2] == 0)
                {
                    textLength -= 2;
                }

                return Encoding.Unicode.GetString(bytes, 0, textLength);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private PdfDestination? ResolveDestination(FpdfBookmarkT bookmark)
        {
            var destination = fpdf_doc.FPDFBookmarkGetDest(_document, bookmark);
            if (destination is null)
            {
                var action = fpdf_doc.FPDFBookmarkGetAction(bookmark);
                if (action is not null && fpdf_doc.FPDFActionGetType(action) == 1)
                {
                    destination = fpdf_doc.FPDFActionGetDest(_document, action);
                }
            }

            if (destination is null)
            {
                return null;
            }

            var pageIndex = fpdf_doc.FPDFDestGetDestPageIndex(_document, destination);
            if (pageIndex < 0 || pageIndex >= PageCount)
            {
                return null;
            }

            var hasX = 0;
            var hasY = 0;
            var hasZoom = 0;
            var x = 0f;
            var y = 0f;
            var zoom = 0f;
            var hasLocation = fpdf_doc.FPDFDestGetLocationInPage(
                destination,
                ref hasX,
                ref hasY,
                ref hasZoom,
                ref x,
                ref y,
                ref zoom) != 0;
            return new PdfDestination(
                pageIndex,
                hasLocation && hasX != 0 ? x : null,
                hasLocation && hasY != 0 ? y : null,
                hasLocation && hasZoom != 0 && zoom > 0 ? zoom : null);
        }

        private string ReadTextPage(int pageIndex)
        {
            var page = LoadPage(pageIndex);
            FpdfTextpageT? textPage = null;
            try
            {
                textPage = fpdf_text.FPDFTextLoadPage(page);
                if (textPage is null)
                {
                    throw new InvalidDataException($"PDFium không đọc được text của trang {pageIndex + 1}.");
                }

                var count = fpdf_text.FPDFTextCountChars(textPage);
                if (count < 0 || count > MaximumTextCharactersPerPage)
                {
                    throw new InvalidDataException($"Số ký tự của trang {pageIndex + 1} không hợp lệ.");
                }

                if (count == 0)
                {
                    return string.Empty;
                }

                var buffer = new ushort[checked(count + 1)];
                var written = fpdf_text.FPDFTextGetText(textPage, 0, count, ref buffer[0]);
                var textLength = Math.Clamp(written - 1, 0, count);
                return new string(MemoryMarshal.Cast<ushort, char>(buffer.AsSpan(0, textLength)));
            }
            finally
            {
                if (textPage is not null)
                {
                    fpdf_text.FPDFTextClosePage(textPage);
                }

                fpdfview.FPDF_ClosePage(page);
            }
        }

        private IReadOnlyList<PdfPageRect> ReadTextBounds(int pageIndex, int startIndex, int length)
        {
            var page = LoadPage(pageIndex);
            FpdfTextpageT? textPage = null;
            try
            {
                textPage = fpdf_text.FPDFTextLoadPage(page);
                if (textPage is null)
                {
                    return Array.Empty<PdfPageRect>();
                }

                var firstCharacter = fpdf_searchex.FPDFTextGetCharIndexFromTextIndex(textPage, startIndex);
                var lastCharacter = fpdf_searchex.FPDFTextGetCharIndexFromTextIndex(
                    textPage,
                    checked(startIndex + length - 1));
                if (firstCharacter < 0 || lastCharacter < firstCharacter)
                {
                    return Array.Empty<PdfPageRect>();
                }

                var rectangleCount = fpdf_text.FPDFTextCountRects(
                    textPage,
                    firstCharacter,
                    checked(lastCharacter - firstCharacter + 1));
                if (rectangleCount <= 0)
                {
                    return Array.Empty<PdfPageRect>();
                }

                rectangleCount = Math.Min(rectangleCount, MaximumTextRectanglesPerRequest);
                var pageHeight = fpdfview.FPDF_GetPageHeightF(page);
                var rectangles = new List<PdfPageRect>(rectangleCount);
                for (var index = 0; index < rectangleCount; index++)
                {
                    var left = 0d;
                    var top = 0d;
                    var right = 0d;
                    var bottom = 0d;
                    if (fpdf_text.FPDFTextGetRect(
                        textPage,
                        index,
                        ref left,
                        ref top,
                        ref right,
                        ref bottom) == 0)
                    {
                        continue;
                    }

                    var normalizedTop = pageHeight - top;
                    var normalizedBottom = pageHeight - bottom;
                    if (double.IsFinite(left)
                        && double.IsFinite(right)
                        && double.IsFinite(normalizedTop)
                        && double.IsFinite(normalizedBottom))
                    {
                        rectangles.Add(new PdfPageRect(left, normalizedTop, right, normalizedBottom));
                    }
                }

                return rectangles;
            }
            finally
            {
                if (textPage is not null)
                {
                    fpdf_text.FPDFTextClosePage(textPage);
                }

                fpdfview.FPDF_ClosePage(page);
            }
        }

        private FpdfPageT LoadPage(int pageIndex)
        {
            var page = fpdfview.FPDF_LoadPage(_document, pageIndex);
            return page ?? throw new InvalidDataException($"PDFium không tải được trang {pageIndex + 1}.");
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
}
