using System.Text.Json;

namespace MedNote.Core;

public enum PdfFitMode
{
    Page,
    Width,
}

public enum PdfViewMode
{
    Single,
    Continuous,
}

public enum PdfTool
{
    Pan,
    Select,
    Highlight,
    Pen,
    Eraser,
    Crop,
}

/// <summary>
/// Reader fields intentionally match the web v6 payload. Annotation JSON is
/// opaque until the native annotation editor is implemented, so importing and
/// saving a document cannot erase annotations created by the web app.
/// </summary>
public sealed record ReaderState
{
    public int Page { get; init; } = 1;

    public double Zoom { get; init; } = 1d;

    public PdfFitMode FitMode { get; init; } = PdfFitMode.Page;

    public int Rotation { get; init; }

    public PdfViewMode ViewMode { get; init; } = PdfViewMode.Single;

    public List<int> Bookmarks { get; init; } = [];

    public List<JsonElement> Annotations { get; init; } = [];

    public ReaderState Normalize(int pageCount)
    {
        var maximumPage = Math.Max(1, pageCount);

        return this with
        {
            Page = ReaderMath.ClampPage(Page, maximumPage),
            Zoom = ReaderMath.ClampZoom(Zoom),
            Rotation = ReaderMath.NormalizeRotation(Rotation),
            Bookmarks = Bookmarks
                .Where(page => page >= 1 && page <= maximumPage)
                .Distinct()
                .Order()
                .ToList(),
            Annotations = Annotations ?? [],
        };
    }
}

public sealed record ReaderPosition
{
    public int AnchorPage { get; init; } = 1;

    public double PageOffsetRatio { get; init; }

    public double HorizontalOffset { get; init; }

    public ReaderPosition Normalize(int pageCount) => this with
    {
        AnchorPage = ReaderMath.ClampPage(AnchorPage, pageCount),
        PageOffsetRatio = Math.Clamp(double.IsFinite(PageOffsetRatio) ? PageOffsetRatio : 0d, 0d, 1d),
        HorizontalOffset = Math.Max(0d, double.IsFinite(HorizontalOffset) ? HorizontalOffset : 0d),
    };
}

public static class ReaderMath
{
    public const double MinimumZoom = 0.55d;
    public const double MaximumZoom = 2.5d;

    public static int ClampPage(int page, int pageCount) => Math.Clamp(page, 1, Math.Max(1, pageCount));

    public static double ClampZoom(double zoom) => Math.Clamp(double.IsFinite(zoom) ? zoom : 1d, MinimumZoom, MaximumZoom);

    public static int NormalizeRotation(int rotation)
    {
        var normalized = ((rotation % 360) + 360) % 360;
        return (int)Math.Round(normalized / 90d) * 90 % 360;
    }

    public static double StepZoom(double zoom, int direction)
    {
        var delta = direction < 0 ? -0.1d : 0.1d;
        return Math.Round(ClampZoom(zoom + delta), 2, MidpointRounding.AwayFromZero);
    }

    public static double ContinuousAnchorCorrection(double containerTop, double containerHeight, double pageOffsetRatio) =>
        containerTop
        + Math.Clamp(double.IsFinite(pageOffsetRatio) ? pageOffsetRatio : 0d, 0d, 1d)
        * Math.Max(1d, containerHeight);
}
