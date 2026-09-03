using System.Runtime.InteropServices;
using MedNote.Core;
using PDFiumCore;

namespace MedNote.Windows.App.Infrastructure;

internal static class PdfiumPageRenderer
{
    private const int MaximumRenderEdge = 4_096;

    public static RenderedPdfPage Render(
        FpdfDocumentT document,
        int pageIndex,
        uint requestedWidth,
        uint requestedHeight,
        int requestedRotation)
    {
        var page = LoadPage(document, pageIndex);
        FpdfBitmapT? bitmap = null;
        try
        {
            var pageWidth = Math.Max(1d, fpdfview.FPDF_GetPageWidthF(page));
            var pageHeight = Math.Max(1d, fpdfview.FPDF_GetPageHeightF(page));
            var rotation = ReaderMath.NormalizeRotation(requestedRotation);
            var rotatedAspectRatio = rotation is 90 or 270
                ? pageHeight / pageWidth
                : pageWidth / pageHeight;
            var rawWidth = Math.Max(64d, requestedWidth);
            var rawHeight = Math.Max(
                64d,
                requestedHeight > 0 ? requestedHeight : rawWidth / rotatedAspectRatio);
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
            fpdfview.FPDF_RenderPageBitmap(
                bitmap,
                page,
                0,
                0,
                width,
                height,
                rotation / 90,
                (int)flags);

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

    private static FpdfPageT LoadPage(FpdfDocumentT document, int pageIndex)
    {
        var page = fpdfview.FPDF_LoadPage(document, pageIndex);
        return page ?? throw new InvalidDataException($"PDFium không tải được trang {pageIndex + 1}.");
    }
}
