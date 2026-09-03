using System.Runtime.InteropServices;
using MedNote.Core;
using PDFiumCore;

namespace MedNote.Windows.App.Infrastructure;

internal static class PdfiumTextReader
{
    private const int MaximumTextCharactersPerPage = 8_000_000;
    private const int MaximumTextRectanglesPerRequest = 100_000;

    public static string ReadPage(FpdfDocumentT document, int pageIndex)
    {
        var page = LoadPage(document, pageIndex);
        FpdfTextpageT? textPage = null;
        try
        {
            textPage = fpdf_text.FPDFTextLoadPage(page);
            if (textPage is null)
            {
                throw new InvalidDataException($"PDFium không đọc được text của trang {pageIndex + 1}.");
            }

            var count = fpdf_text.FPDFTextCountChars(textPage);
            if (count < 0 || count > MaximumTextCharactersPerPage)
            {
                throw new InvalidDataException($"Số ký tự của trang {pageIndex + 1} không hợp lệ.");
            }

            if (count == 0)
            {
                return string.Empty;
            }

            var buffer = new ushort[checked(count + 1)];
            var written = fpdf_text.FPDFTextGetText(textPage, 0, count, ref buffer[0]);
            var textLength = Math.Clamp(written - 1, 0, count);
            return new string(MemoryMarshal.Cast<ushort, char>(buffer.AsSpan(0, textLength)));
        }
        finally
        {
            if (textPage is not null)
            {
                fpdf_text.FPDFTextClosePage(textPage);
            }

            fpdfview.FPDF_ClosePage(page);
        }
    }

    public static IReadOnlyList<PdfPageRect> ReadBounds(
        FpdfDocumentT document,
        int pageIndex,
        int startIndex,
        int length)
    {
        var page = LoadPage(document, pageIndex);
        FpdfTextpageT? textPage = null;
        try
        {
            textPage = fpdf_text.FPDFTextLoadPage(page);
            if (textPage is null)
            {
                return Array.Empty<PdfPageRect>();
            }

            var firstCharacter = fpdf_searchex.FPDFTextGetCharIndexFromTextIndex(textPage, startIndex);
            var lastCharacter = fpdf_searchex.FPDFTextGetCharIndexFromTextIndex(
                textPage,
                checked(startIndex + length - 1));
            if (firstCharacter < 0 || lastCharacter < firstCharacter)
            {
                return Array.Empty<PdfPageRect>();
            }

            var rectangleCount = fpdf_text.FPDFTextCountRects(
                textPage,
                firstCharacter,
                checked(lastCharacter - firstCharacter + 1));
            if (rectangleCount <= 0)
            {
                return Array.Empty<PdfPageRect>();
            }

            rectangleCount = Math.Min(rectangleCount, MaximumTextRectanglesPerRequest);
            var pageHeight = fpdfview.FPDF_GetPageHeightF(page);
            var rectangles = new List<PdfPageRect>(rectangleCount);
            for (var index = 0; index < rectangleCount; index++)
            {
                var left = 0d;
                var top = 0d;
                var right = 0d;
                var bottom = 0d;
                if (fpdf_text.FPDFTextGetRect(
                    textPage,
                    index,
                    ref left,
                    ref top,
                    ref right,
                    ref bottom) == 0)
                {
                    continue;
                }

                var normalizedTop = pageHeight - top;
                var normalizedBottom = pageHeight - bottom;
                if (double.IsFinite(left)
                    && double.IsFinite(right)
                    && double.IsFinite(normalizedTop)
                    && double.IsFinite(normalizedBottom))
                {
                    rectangles.Add(new PdfPageRect(left, normalizedTop, right, normalizedBottom));
                }
            }

            return rectangles;
        }
        finally
        {
            if (textPage is not null)
            {
                fpdf_text.FPDFTextClosePage(textPage);
            }

            fpdfview.FPDF_ClosePage(page);
        }
    }

    public static int? ReadIndexAtPoint(
        FpdfDocumentT document,
        int pageIndex,
        PdfPagePoint point,
        double horizontalTolerance,
        double verticalTolerance)
    {
        var page = LoadPage(document, pageIndex);
        FpdfTextpageT? textPage = null;
        try
        {
            textPage = fpdf_text.FPDFTextLoadPage(page);
            if (textPage is null)
            {
                return null;
            }

            var pageHeight = fpdfview.FPDF_GetPageHeightF(page);
            var characterIndex = fpdf_text.FPDFTextGetCharIndexAtPos(
                textPage,
                point.X,
                pageHeight - point.Y,
                horizontalTolerance,
                verticalTolerance);
            if (characterIndex < 0)
            {
                return null;
            }

            var textIndex = fpdf_searchex.FPDFTextGetTextIndexFromCharIndex(textPage, characterIndex);
            return textIndex >= 0 ? textIndex : null;
        }
        finally
        {
            if (textPage is not null)
            {
                fpdf_text.FPDFTextClosePage(textPage);
            }

            fpdfview.FPDF_ClosePage(page);
        }
    }

    private static FpdfPageT LoadPage(FpdfDocumentT document, int pageIndex)
    {
        var page = fpdfview.FPDF_LoadPage(document, pageIndex);
        return page ?? throw new InvalidDataException($"PDFium không tải được trang {pageIndex + 1}.");
    }
}
