using MedNote.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Windows.Graphics.DirectX;

namespace MedNote.Windows.App.Infrastructure;

/// <summary>
/// Uploads PDFium's tightly-packed BGRA buffer directly to a Direct2D surface.
/// No image-encoding round trip exists in the display path.
/// </summary>
internal static class Direct2DPageSurfaceFactory
{
    public static CanvasImageSource Create(RenderedPdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!page.HasValidBuffer || page.PixelWidth > int.MaxValue || page.PixelHeight > int.MaxValue)
        {
            throw new InvalidDataException("Không thể tạo bề mặt Direct2D từ buffer PDFium.");
        }

        var bytes = GetTightlyPackedBytes(page);
        var device = CanvasDevice.GetSharedDevice();
        using var bitmap = CanvasBitmap.CreateFromBytes(
            device,
            bytes,
            checked((int)page.PixelWidth),
            checked((int)page.PixelHeight),
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            96f,
            CanvasAlphaMode.Ignore);
        var imageSource = new CanvasImageSource(
            device,
            page.PixelWidth,
            page.PixelHeight,
            96f,
            CanvasAlphaMode.Ignore);
        using (var drawing = imageSource.CreateDrawingSession(Colors.White))
        {
            drawing.DrawImage(bitmap);
        }

        return imageSource;
    }

    private static byte[] GetTightlyPackedBytes(RenderedPdfPage page)
    {
        var rowBytes = checked(page.PixelWidth * 4u);
        if (page.Stride == rowBytes)
        {
            return page.BgraBytes;
        }

        var packed = new byte[checked((int)((long)rowBytes * page.PixelHeight))];
        for (var row = 0u; row < page.PixelHeight; row++)
        {
            Buffer.BlockCopy(
                page.BgraBytes,
                checked((int)((long)row * page.Stride)),
                packed,
                checked((int)((long)row * rowBytes)),
                checked((int)rowBytes));
        }

        return packed;
    }
}
