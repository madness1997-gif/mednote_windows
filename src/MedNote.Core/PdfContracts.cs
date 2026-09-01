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

public sealed record RenderedPdfPage(byte[] PngBytes, uint PixelWidth, uint PixelHeight)
{
    public long EstimatedBitmapBytes => checked((long)PixelWidth * PixelHeight * 4L);
}

public interface IPdfDocumentSession : IAsyncDisposable
{
    string Path { get; }

    int PageCount { get; }

    ValueTask<PdfPageMetrics> GetPageMetricsAsync(int pageIndex, CancellationToken cancellationToken = default);

    ValueTask<RenderedPdfPage> RenderPageAsync(PdfRenderRequest request, CancellationToken cancellationToken = default);
}

public interface IPdfEngine
{
    ValueTask<IPdfDocumentSession> OpenAsync(string path, CancellationToken cancellationToken = default);
}
