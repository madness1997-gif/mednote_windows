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
    private static readonly object DeviceGate = new();
    private static CanvasDevice? _device;

    public static event EventHandler? SurfacesInvalidated;

    public static CanvasImageSource Create(RenderedPdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!page.HasValidBuffer || page.PixelWidth > int.MaxValue || page.PixelHeight > int.MaxValue)
        {
            throw new InvalidDataException("Không thể tạo bề mặt Direct2D từ buffer PDFium.");
        }

        var device = GetSharedDevice();
        try
        {
            var bytes = GetTightlyPackedBytes(page);
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
        catch (Exception exception) when (device.IsDeviceLost(exception.HResult))
        {
            try
            {
                device.RaiseDeviceLost();
            }
            catch
            {
                // Preserve the original Direct2D failure for the presenter.
            }

            throw;
        }
    }

    public static void RequestSurfaceRecreation()
    {
        var handlers = SurfacesInvalidated;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(null, EventArgs.Empty);
            }
            catch
            {
                // One recycled presenter must not block the remaining pages.
            }
        }
    }

    private static CanvasDevice GetSharedDevice()
    {
        lock (DeviceGate)
        {
            var sharedDevice = CanvasDevice.GetSharedDevice();
            if (ReferenceEquals(_device, sharedDevice))
            {
                return sharedDevice;
            }

            if (_device is not null)
            {
                _device.DeviceLost -= OnDeviceLost;
            }

            _device = sharedDevice;
            _device.DeviceLost += OnDeviceLost;
            return sharedDevice;
        }
    }

    private static void OnDeviceLost(CanvasDevice sender, object args)
    {
        lock (DeviceGate)
        {
            if (ReferenceEquals(_device, sender))
            {
                _device.DeviceLost -= OnDeviceLost;
                _device = null;
            }
        }

        RequestSurfaceRecreation();
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
