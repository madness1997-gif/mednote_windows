namespace MedNote.Core;

public readonly record struct PdfPageMetrics(double Width, double Height)
{
    private const double DefaultAspectRatio = 1d / 1.4142135623730951d;

    /// <summary>
    /// A display-safe width/height ratio. A small number of real-world PDFs
    /// expose broken crop/media boxes through native PDF engines (for example
    /// a normal article page reported as an extremely narrow strip). Rendering
    /// those raw metrics makes the page unusable, so fall back to A-series
    /// paper for non-finite or implausible values.
    /// </summary>
    public double AspectRatio
    {
        get
        {
            var ratio = Width / Height;
            return double.IsFinite(ratio) && ratio is >= 0.25d and <= 4d
                ? ratio
                : DefaultAspectRatio;
        }
    }
}

public readonly record struct PdfRenderRequest(int PageIndex, uint PixelWidth, uint PixelHeight);

public sealed record RenderedPdfPage(byte[] BgraBytes, uint PixelWidth, uint PixelHeight, uint Stride)
{
    public RenderedPdfPage(byte[] bgraBytes, uint pixelWidth, uint pixelHeight)
        : this(bgraBytes, pixelWidth, pixelHeight, checked(pixelWidth * 4u))
    {
    }

    public long EstimatedBitmapBytes => checked((long)Stride * PixelHeight);

    // A realized Direct2D page owns both the managed upload buffer and a GPU
    // surface. Count both so the cache budget remains conservative.
    public long EstimatedResidentBytes => checked(EstimatedBitmapBytes * 2L);

    public bool HasValidBuffer =>
        PixelWidth > 0
        && PixelHeight > 0
        && (long)Stride >= checked((long)PixelWidth * 4L)
        && BgraBytes.LongLength >= checked((long)Stride * PixelHeight);
}

public sealed class PdfPasswordRequiredException(string message) : IOException(message)
{
}

public interface IPdfDocumentSession : IAsyncDisposable
{
    string Path { get; }

    int PageCount { get; }

    IReadOnlyList<PdfPageMetrics> PageMetrics { get; }

    ValueTask<PdfPageMetrics> GetPageMetricsAsync(int pageIndex, CancellationToken cancellationToken = default);

    ValueTask<RenderedPdfPage> RenderPageAsync(PdfRenderRequest request, CancellationToken cancellationToken = default);
}

public interface IPdfEngine
{
    ValueTask<IPdfDocumentSession> OpenAsync(string path, CancellationToken cancellationToken = default);

    ValueTask<IPdfDocumentSession> OpenAsync(
        string path,
        string? password,
        CancellationToken cancellationToken = default) =>
        string.IsNullOrEmpty(password)
            ? OpenAsync(path, cancellationToken)
            : ValueTask.FromException<IPdfDocumentSession>(
                new NotSupportedException("PDF engine này chưa hỗ trợ tài liệu có mật khẩu."));
}
