namespace MedNote.Core;

public readonly record struct PdfPagePoint(double X, double Y);

public readonly record struct PdfPageRect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Math.Max(0d, Right - Left);

    public double Height => Math.Max(0d, Bottom - Top);
}

public readonly record struct PdfDestination(int PageIndex, double? X = null, double? Y = null, double? Zoom = null)
{
    public PdfDestination Normalize(int pageCount) => this with
    {
        PageIndex = Math.Clamp(PageIndex, 0, Math.Max(0, pageCount - 1)),
        Zoom = Zoom is null ? null : ReaderMath.ClampZoom(Zoom.Value),
    };
}

public sealed record PdfOutlineNode
{
    public PdfOutlineNode(
        string title,
        PdfDestination? destination = null,
        IReadOnlyList<PdfOutlineNode>? children = null,
        bool isExpandedByDefault = false)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "Mục không tên" : title.Trim();
        Destination = destination;
        Children = children ?? Array.Empty<PdfOutlineNode>();
        IsExpandedByDefault = isExpandedByDefault;
    }

    public string Title { get; init; }

    public PdfDestination? Destination { get; init; }

    public IReadOnlyList<PdfOutlineNode> Children { get; init; }

    public bool IsExpandedByDefault { get; init; }
}

public sealed record PdfTextPage(int PageIndex, string Text)
{
    public long EstimatedCacheBytes => 64L + (Text?.Length ?? 0) * sizeof(char);
}

public interface IPdfOutlineProvider
{
    ValueTask<IReadOnlyList<PdfOutlineNode>> GetOutlineAsync(CancellationToken cancellationToken = default);
}

public interface IPdfTextProvider
{
    int PageCount { get; }

    ValueTask<PdfTextPage> GetTextPageAsync(int pageIndex, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<PdfPageRect>> GetTextBoundsAsync(
        int pageIndex,
        int startIndex,
        int length,
        CancellationToken cancellationToken = default);
}
