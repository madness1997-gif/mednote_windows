namespace MedNote.Core;

public sealed record PdfTextExcerpt(
    string DocumentId, string DocumentName, int Page, PdfAnnotationRect Rect, string Text);
