namespace MedNote.Core;

public readonly record struct PdfPageMetrics(double Width, double Height)
{
    public double AspectRatio => Width <= 0 || Height <= 0 ? 1d / Math.Sqrt(2d) : Width / Height;
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
