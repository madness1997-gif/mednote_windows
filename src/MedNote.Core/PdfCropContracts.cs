namespace MedNote.Core;

public sealed record PdfCropRequest(
    int PageIndex,
    PdfAnnotationRect Rect,
    int Rotation = 0,
    uint MaximumPixelEdge = 2_048);

public sealed record PdfCropResult(
    int Page,
    PdfAnnotationRect Rect,
    byte[] ImageBytes,
    string ContentType,
    uint PixelWidth,
    uint PixelHeight);

public interface IPdfCropProvider
{
    ValueTask<PdfCropResult> CropAsync(
        PdfCropRequest request,
        CancellationToken cancellationToken = default);
}

public interface IPdfAnnotationExportProvider
{
    ValueTask ExportFlattenedAsync(
        string outputPath,
        IReadOnlyList<PdfAnnotation> annotations,
        CancellationToken cancellationToken = default);
}
