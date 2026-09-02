namespace MedNote.Core;

/// <summary>
/// Maps PDF text geometry to and from the displayed page after the Reader's
/// additional clockwise rotation. Intrinsic PDF rotation is already folded
/// into the page metrics by PDFium.
/// </summary>
public static class PdfPageCoordinateMapper
{
    public static PdfPagePoint DisplayToPage(
        PdfPagePoint displayPoint,
        PdfPageMetrics page,
        double displayWidth,
        double displayHeight,
        int rotation)
    {
        ValidateDimensions(page, displayWidth, displayHeight);
        rotation = ReaderMath.NormalizeRotation(rotation);
        var rotatedWidth = rotation is 90 or 270 ? page.Height : page.Width;
        var rotatedHeight = rotation is 90 or 270 ? page.Width : page.Height;
        var rotatedX = Math.Clamp(displayPoint.X, 0d, displayWidth) * rotatedWidth / displayWidth;
        var rotatedY = Math.Clamp(displayPoint.Y, 0d, displayHeight) * rotatedHeight / displayHeight;

        return rotation switch
        {
            90 => new PdfPagePoint(rotatedY, page.Height - rotatedX),
            180 => new PdfPagePoint(page.Width - rotatedX, page.Height - rotatedY),
            270 => new PdfPagePoint(page.Width - rotatedY, rotatedX),
            _ => new PdfPagePoint(rotatedX, rotatedY),
        };
    }

    public static PdfPageRect PageToDisplay(
        PdfPageRect pageRect,
        PdfPageMetrics page,
        double displayWidth,
        double displayHeight,
        int rotation)
    {
        ValidateDimensions(page, displayWidth, displayHeight);
        var corners = new[]
        {
            PageToRotated(new PdfPagePoint(pageRect.Left, pageRect.Top), page, rotation),
            PageToRotated(new PdfPagePoint(pageRect.Right, pageRect.Top), page, rotation),
            PageToRotated(new PdfPagePoint(pageRect.Left, pageRect.Bottom), page, rotation),
            PageToRotated(new PdfPagePoint(pageRect.Right, pageRect.Bottom), page, rotation),
        };
        rotation = ReaderMath.NormalizeRotation(rotation);
        var rotatedWidth = rotation is 90 or 270 ? page.Height : page.Width;
        var rotatedHeight = rotation is 90 or 270 ? page.Width : page.Height;
        var left = corners.Min(point => point.X) * displayWidth / rotatedWidth;
        var top = corners.Min(point => point.Y) * displayHeight / rotatedHeight;
        var right = corners.Max(point => point.X) * displayWidth / rotatedWidth;
        var bottom = corners.Max(point => point.Y) * displayHeight / rotatedHeight;
        return new PdfPageRect(left, top, right, bottom);
    }

    private static PdfPagePoint PageToRotated(PdfPagePoint point, PdfPageMetrics page, int rotation) =>
        ReaderMath.NormalizeRotation(rotation) switch
        {
            90 => new PdfPagePoint(page.Height - point.Y, point.X),
            180 => new PdfPagePoint(page.Width - point.X, page.Height - point.Y),
            270 => new PdfPagePoint(point.Y, page.Width - point.X),
            _ => point,
        };

    private static void ValidateDimensions(PdfPageMetrics page, double displayWidth, double displayHeight)
    {
        if (!double.IsFinite(page.Width) || page.Width <= 0d
            || !double.IsFinite(page.Height) || page.Height <= 0d
            || !double.IsFinite(displayWidth) || displayWidth <= 0d
            || !double.IsFinite(displayHeight) || displayHeight <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "Kích thước trang và vùng hiển thị phải hữu hạn và dương.");
        }
    }
}
