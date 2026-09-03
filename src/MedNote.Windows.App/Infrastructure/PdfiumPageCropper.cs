using System.Runtime.InteropServices;
using MedNote.Core;
using PDFiumCore;

namespace MedNote.Windows.App.Infrastructure;

internal static class PdfiumPageCropper
{
    private const double MaximumFullPageRenderEdge = 32_768d;

    public static PdfCropResult Crop(
        FpdfDocumentT document,
        IReadOnlyList<PdfPageMetrics> pageMetrics,
        PdfCropRequest request)
    {
        var metrics = pageMetrics[request.PageIndex];
        var rect = Clamp(request.Rect.Normalize(), metrics);
        if (rect.Width < 0.5d || rect.Height < 0.5d)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Vùng crop PDF quá nhỏ.");
        }

        var rotation = ReaderMath.NormalizeRotation(request.Rotation);
        var rotatedWidth = rotation is 90 or 270 ? metrics.Height : metrics.Width;
        var rotatedHeight = rotation is 90 or 270 ? metrics.Width : metrics.Height;
        var displayRect = PdfAnnotationCoordinateMapper.AnnotationToDisplay(
            rect,
            metrics,
            rotatedWidth,
            rotatedHeight,
            rotation);
        var maximumEdge = Math.Clamp(request.MaximumPixelEdge, 256u, 4_096u);
        var scale = maximumEdge / Math.Max(displayRect.Width, displayRect.Height);
        scale = Math.Min(
            scale,
            MaximumFullPageRenderEdge / Math.Max(rotatedWidth, rotatedHeight));

        var outputWidth = checked((int)Math.Clamp(Math.Round(displayRect.Width * scale), 1d, maximumEdge));
        var outputHeight = checked((int)Math.Clamp(Math.Round(displayRect.Height * scale), 1d, maximumEdge));
        var fullWidth = checked((int)Math.Clamp(Math.Round(rotatedWidth * scale), 1d, MaximumFullPageRenderEdge));
        var fullHeight = checked((int)Math.Clamp(Math.Round(rotatedHeight * scale), 1d, MaximumFullPageRenderEdge));
        var offsetX = checked(-(int)Math.Round(displayRect.Left * scale));
        var offsetY = checked(-(int)Math.Round(displayRect.Top * scale));

        var page = fpdfview.FPDF_LoadPage(document, request.PageIndex)
            ?? throw new InvalidDataException($"PDFium không tải được trang {request.PageIndex + 1} để crop.");
        FpdfBitmapT? bitmap = null;
        try
        {
            bitmap = fpdfview.FPDFBitmapCreateEx(
                outputWidth,
                outputHeight,
                (int)FPDFBitmapFormat.BGRA,
                IntPtr.Zero,
                0);
            if (bitmap is null)
            {
                throw new OutOfMemoryException("PDFium không tạo được bitmap crop.");
            }

            fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, outputWidth, outputHeight, uint.MaxValue);
            var flags = RenderFlags.RenderAnnotations
                | RenderFlags.OptimizeTextForLcd
                | RenderFlags.LimitImageCacheSize;
            fpdfview.FPDF_RenderPageBitmap(
                bitmap,
                page,
                offsetX,
                offsetY,
                fullWidth,
                fullHeight,
                rotation / 90,
                (int)flags);

            var buffer = fpdfview.FPDFBitmapGetBuffer(bitmap);
            var sourceStride = fpdfview.FPDFBitmapGetStride(bitmap);
            var targetStride = checked(outputWidth * 4);
            if (buffer == IntPtr.Zero || sourceStride < targetStride)
            {
                throw new InvalidDataException("PDFium trả về buffer crop không hợp lệ.");
            }

            var pixels = new byte[checked(targetStride * outputHeight)];
            for (var row = 0; row < outputHeight; row++)
            {
                Marshal.Copy(
                    IntPtr.Add(buffer, checked(row * sourceStride)),
                    pixels,
                    checked(row * targetStride),
                    targetStride);
            }

            var png = PngImageEncoder.EncodeBgra(
                pixels,
                checked((uint)outputWidth),
                checked((uint)outputHeight),
                checked((uint)targetStride));
            return new PdfCropResult(
                request.PageIndex + 1,
                rect,
                png,
                "image/png",
                checked((uint)outputWidth),
                checked((uint)outputHeight));
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

    private static PdfAnnotationRect Clamp(PdfAnnotationRect rect, PdfPageMetrics metrics) => new(
        Math.Clamp(rect.Left, 0d, metrics.Width),
        Math.Clamp(rect.Bottom, 0d, metrics.Height),
        Math.Clamp(rect.Right, 0d, metrics.Width),
        Math.Clamp(rect.Top, 0d, metrics.Height));
}
