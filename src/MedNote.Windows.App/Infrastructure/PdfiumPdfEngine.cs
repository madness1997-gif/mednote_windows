using MedNote.Core;
using PDFiumCore;

namespace MedNote.Windows.App.Infrastructure;

/// <summary>
/// Owns PDFium process lifetime and opens document sessions. Rendering, text
/// extraction and outline traversal live behind the session/helper boundary so
/// this adapter stays focused on engine lifecycle.
/// </summary>
public sealed class PdfiumPdfEngine : IPdfEngine, IAsyncDisposable
{
    private readonly PdfiumDispatcher _dispatcher = new();
    private int _disposed;

    public ValueTask<IPdfDocumentSession> OpenAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        OpenAsync(path, password: null, cancellationToken: cancellationToken);

    public async ValueTask<IPdfDocumentSession> OpenAsync(
        string path,
        string? password,
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
                var document = fpdfview.FPDF_LoadDocument(fullPath, password);
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
        4 => new PdfPasswordRequiredException("PDF cần mật khẩu hoặc mật khẩu vừa nhập không đúng."),
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
}
