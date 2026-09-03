using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using MedNote.Core;
using PDFiumCore;

namespace MedNote.Windows.App.Infrastructure;

internal static class PdfiumAnnotationExporter
{
    private const int FillModeNone = 0;
    private const int FillModeWinding = 2;
    private const int LineCapRound = 1;
    private const int LineJoinRound = 1;

    public static void Export(
        string sourcePath,
        string? password,
        string outputPath,
        IReadOnlyList<PdfAnnotation> annotations,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(annotations);

        var source = System.IO.Path.GetFullPath(sourcePath);
        var destination = System.IO.Path.GetFullPath(outputPath);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Hãy xuất sang một tệp mới; PDF nguồn đang mở sẽ không bị ghi đè.");
        }

        var destinationDirectory = System.IO.Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("Không xác định được thư mục xuất PDF.");
        Directory.CreateDirectory(destinationDirectory);
        var temporaryPath = System.IO.Path.Combine(
            destinationDirectory,
            $".{System.IO.Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        var document = fpdfview.FPDF_LoadDocument(source, password);
        if (document is null)
        {
            throw CreateLoadException(fpdfview.FPDF_GetLastError());
        }

        try
        {
            var pageCount = fpdfview.FPDF_GetPageCount(document);
            foreach (var pageGroup in annotations
                .Where(annotation => annotation.Page >= 1 && annotation.Page <= pageCount)
                .GroupBy(annotation => annotation.Page)
                .OrderBy(group => group.Key))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = fpdfview.FPDF_LoadPage(document, pageGroup.Key - 1)
                    ?? throw new InvalidDataException($"PDFium không tải được trang {pageGroup.Key} để xuất.");
                try
                {
                    foreach (var annotation in pageGroup)
                    {
                        AddAnnotation(document, page, annotation);
                    }

                    if (fpdf_edit.FPDFPageGenerateContent(page) == 0)
                    {
                        throw new InvalidDataException($"PDFium không tạo được nội dung trang {pageGroup.Key}.");
                    }
                }
                finally
                {
                    fpdfview.FPDF_ClosePage(page);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.WriteThrough);
            using var writer = new FPDF_FILEWRITE_ { Version = 1 };
            PDFiumCore.Delegates.Func_int___IntPtr___IntPtr_ulong writeBlock = (_, data, size) =>
            {
                try
                {
                    var length = checked((int)size);
                    var buffer = new byte[length];
                    Marshal.Copy(data, buffer, 0, length);
                    output.Write(buffer, 0, length);
                    return 1;
                }
                catch
                {
                    return 0;
                }
            };
            writer.WriteBlock = writeBlock;
            if (fpdf_save.FPDF_SaveAsCopy(document, writer, 0) == 0)
            {
                throw new IOException("PDFium không ghi được bản PDF có chú thích.");
            }

            output.Flush(flushToDisk: true);
            GC.KeepAlive(writeBlock);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
        finally
        {
            fpdfview.FPDF_CloseDocument(document);
        }

        File.Move(temporaryPath, destination, overwrite: true);
    }

    private static void AddAnnotation(
        FpdfDocumentT document,
        FpdfPageT page,
        PdfAnnotation annotation)
    {
        var color = ParseColor(annotation.Color);
        switch (annotation.Kind)
        {
            case PdfAnnotationKind.Highlight:
            case PdfAnnotationKind.AreaHighlight:
                foreach (var rect in annotation.Rects ?? [])
                {
                    InsertFilledRectangle(page, rect, color, 87);
                }

                break;
            case PdfAnnotationKind.Underline:
            case PdfAnnotationKind.Strikeout:
            case PdfAnnotationKind.Squiggly:
                foreach (var rect in annotation.Rects ?? [])
                {
                    AddMarkupLine(page, rect, annotation.Kind, color);
                }

                break;
            case PdfAnnotationKind.Ink:
                InsertPolyline(page, annotation.Points ?? [], color, annotation.Width ?? 1d);
                break;
            case PdfAnnotationKind.Rectangle:
                if (annotation.Rect is { } rectangle)
                {
                    InsertRectangleOutline(page, rectangle, color, annotation.Width ?? 1d);
                }

                break;
            case PdfAnnotationKind.Ellipse:
                if (annotation.Rect is { } ellipse)
                {
                    InsertEllipse(page, ellipse, color, annotation.Width ?? 1d);
                }

                break;
            case PdfAnnotationKind.Arrow:
                if (annotation.Rect is { } arrow)
                {
                    InsertArrow(page, arrow, color, annotation.Width ?? 1d);
                }

                break;
            case PdfAnnotationKind.Note:
                if (annotation.Rect is { } note)
                {
                    InsertFilledRectangle(page, note, color, 214);
                    InsertText(document, page, note, "!", color, bold: true);
                }

                break;
            case PdfAnnotationKind.Stamp:
                if (annotation.Rect is { } stamp)
                {
                    InsertRectangleOutline(page, stamp, color, Math.Max(1.4d, annotation.Width ?? 1d));
                    InsertText(document, page, stamp, annotation.Text ?? "DA XEM", color, bold: true);
                }

                break;
            case PdfAnnotationKind.Text:
            case PdfAnnotationKind.Signature:
                if (annotation.Rect is { } text)
                {
                    InsertText(
                        document,
                        page,
                        text,
                        annotation.Text ?? (annotation.Kind == PdfAnnotationKind.Signature ? "Ky ten" : "Ghi chu"),
                        color,
                        italic: annotation.Kind == PdfAnnotationKind.Signature);
                }

                break;
        }
    }

    private static void AddMarkupLine(
        FpdfPageT page,
        PdfAnnotationRect source,
        PdfAnnotationKind kind,
        RgbColor color)
    {
        var rect = source.Normalize();
        if (kind == PdfAnnotationKind.Squiggly)
        {
            var step = Math.Max(2.4d, Math.Min(4d, rect.Height * 0.24d));
            var points = new List<PdfAnnotationPoint>();
            for (var x = rect.Left; x < rect.Right; x += step)
            {
                points.Add(new PdfAnnotationPoint(x, rect.Bottom + 1.2d));
                points.Add(new PdfAnnotationPoint(Math.Min(rect.Right, x + step / 2d), rect.Bottom + 2.8d));
                points.Add(new PdfAnnotationPoint(Math.Min(rect.Right, x + step), rect.Bottom + 1.2d));
            }

            InsertPolyline(page, points, color, 1d);
            return;
        }

        var y = kind == PdfAnnotationKind.Strikeout
            ? rect.Bottom + rect.Height * 0.52d
            : rect.Bottom + 0.6d;
        InsertPolyline(
            page,
            [new PdfAnnotationPoint(rect.Left, y), new PdfAnnotationPoint(rect.Right, y)],
            color,
            1.2d);
    }

    private static void InsertFilledRectangle(
        FpdfPageT page,
        PdfAnnotationRect source,
        RgbColor color,
        uint alpha)
    {
        var rect = source.Normalize();
        if (rect.Width <= 0d || rect.Height <= 0d)
        {
            return;
        }

        var path = fpdf_edit.FPDFPageObjCreateNewRect(
            ToFloat(rect.Left),
            ToFloat(rect.Bottom),
            ToFloat(rect.Width),
            ToFloat(rect.Height));
        ConfigureFill(path, color, alpha);
        Insert(page, path);
    }

    private static void InsertRectangleOutline(
        FpdfPageT page,
        PdfAnnotationRect source,
        RgbColor color,
        double width)
    {
        var rect = source.Normalize();
        if (rect.Width <= 0d || rect.Height <= 0d)
        {
            return;
        }

        var path = fpdf_edit.FPDFPageObjCreateNewRect(
            ToFloat(rect.Left),
            ToFloat(rect.Bottom),
            ToFloat(rect.Width),
            ToFloat(rect.Height));
        ConfigureStroke(path, color, width);
        Insert(page, path);
    }

    private static void InsertEllipse(
        FpdfPageT page,
        PdfAnnotationRect source,
        RgbColor color,
        double width)
    {
        const double kappa = 0.5522847498307936d;
        var rect = source.Normalize();
        var rx = rect.Width / 2d;
        var ry = rect.Height / 2d;
        if (rx <= 0d || ry <= 0d)
        {
            return;
        }

        var cx = rect.Left + rx;
        var cy = rect.Bottom + ry;
        var path = fpdf_edit.FPDFPageObjCreateNewPath(ToFloat(cx + rx), ToFloat(cy));
        if (path is null)
        {
            throw new OutOfMemoryException("PDFium không tạo được ellipse annotation.");
        }

        fpdf_edit.FPDFPathBezierTo(path, ToFloat(cx + rx), ToFloat(cy + kappa * ry), ToFloat(cx + kappa * rx), ToFloat(cy + ry), ToFloat(cx), ToFloat(cy + ry));
        fpdf_edit.FPDFPathBezierTo(path, ToFloat(cx - kappa * rx), ToFloat(cy + ry), ToFloat(cx - rx), ToFloat(cy + kappa * ry), ToFloat(cx - rx), ToFloat(cy));
        fpdf_edit.FPDFPathBezierTo(path, ToFloat(cx - rx), ToFloat(cy - kappa * ry), ToFloat(cx - kappa * rx), ToFloat(cy - ry), ToFloat(cx), ToFloat(cy - ry));
        fpdf_edit.FPDFPathBezierTo(path, ToFloat(cx + kappa * rx), ToFloat(cy - ry), ToFloat(cx + rx), ToFloat(cy - kappa * ry), ToFloat(cx + rx), ToFloat(cy));
        fpdf_edit.FPDFPathClose(path);
        ConfigureStroke(path, color, width);
        Insert(page, path);
    }

    private static void InsertArrow(
        FpdfPageT page,
        PdfAnnotationRect source,
        RgbColor color,
        double width)
    {
        var rect = source.Normalize();
        var start = new PdfAnnotationPoint(rect.Left, rect.Top);
        var end = new PdfAnnotationPoint(rect.Right, rect.Bottom);
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        var head = Math.Min(16d, Math.Max(7d, Math.Min(rect.Width, rect.Height) * 0.28d));
        var left = new PdfAnnotationPoint(
            end.X + Math.Cos(angle + Math.PI * 0.78d) * head,
            end.Y + Math.Sin(angle + Math.PI * 0.78d) * head);
        var right = new PdfAnnotationPoint(
            end.X + Math.Cos(angle - Math.PI * 0.78d) * head,
            end.Y + Math.Sin(angle - Math.PI * 0.78d) * head);
        InsertPolyline(page, [start, end, left, end, right], color, width);
    }

    private static void InsertPolyline(
        FpdfPageT page,
        IReadOnlyList<PdfAnnotationPoint> points,
        RgbColor color,
        double width)
    {
        if (points.Count == 0)
        {
            return;
        }

        var first = points[0].Normalize();
        var path = fpdf_edit.FPDFPageObjCreateNewPath(ToFloat(first.X), ToFloat(first.Y));
        if (path is null)
        {
            throw new OutOfMemoryException("PDFium không tạo được path annotation.");
        }

        for (var index = 1; index < points.Count; index++)
        {
            var point = points[index].Normalize();
            fpdf_edit.FPDFPathLineTo(path, ToFloat(point.X), ToFloat(point.Y));
        }

        ConfigureStroke(path, color, width);
        Insert(page, path);
    }

    private static void InsertText(
        FpdfDocumentT document,
        FpdfPageT page,
        PdfAnnotationRect source,
        string text,
        RgbColor color,
        bool bold = false,
        bool italic = false)
    {
        var rect = source.Normalize();
        var normalized = StandardPdfText(text);
        if (normalized.Length == 0 || rect.Width <= 0d || rect.Height <= 0d)
        {
            return;
        }

        var fontName = bold ? "Helvetica-Bold" : italic ? "Helvetica-Oblique" : "Helvetica";
        var fontSize = ToFloat(Math.Clamp(rect.Height * (italic ? 0.45d : 0.32d), 8d, italic ? 24d : 18d));
        var textObject = fpdf_edit.FPDFPageObjNewTextObj(document, fontName, fontSize);
        if (textObject is null)
        {
            throw new OutOfMemoryException("PDFium không tạo được text annotation.");
        }

        var utf16 = normalized.Select(character => (ushort)character).Append((ushort)0).ToArray();
        if (fpdf_edit.FPDFTextSetText(textObject, ref utf16[0]) == 0)
        {
            fpdf_edit.FPDFPageObjDestroy(textObject);
            throw new InvalidDataException("PDFium không đặt được nội dung text annotation.");
        }

        fpdf_edit.FPDFPageObjSetFillColor(textObject, color.Red, color.Green, color.Blue, 255);
        fpdf_edit.FPDFPageObjTransform(
            textObject,
            1d,
            0d,
            0d,
            1d,
            rect.Left + 3d,
            rect.Bottom + Math.Max(3d, rect.Height - fontSize - 4d));
        Insert(page, textObject);
    }

    private static void ConfigureFill(FpdfPageobjectT path, RgbColor color, uint alpha)
    {
        if (path is null)
        {
            throw new OutOfMemoryException("PDFium không tạo được path annotation.");
        }

        fpdf_edit.FPDFPageObjSetFillColor(path, color.Red, color.Green, color.Blue, alpha);
        fpdf_edit.FPDFPathSetDrawMode(path, FillModeWinding, 0);
    }

    private static void ConfigureStroke(FpdfPageobjectT path, RgbColor color, double width)
    {
        if (path is null)
        {
            throw new OutOfMemoryException("PDFium không tạo được path annotation.");
        }

        fpdf_edit.FPDFPageObjSetStrokeColor(path, color.Red, color.Green, color.Blue, 255);
        fpdf_edit.FPDFPageObjSetStrokeWidth(path, ToFloat(Math.Max(0.1d, width)));
        fpdf_edit.FPDFPageObjSetLineCap(path, LineCapRound);
        fpdf_edit.FPDFPageObjSetLineJoin(path, LineJoinRound);
        fpdf_edit.FPDFPathSetDrawMode(path, FillModeNone, 1);
    }

    private static void Insert(FpdfPageT page, FpdfPageobjectT path)
    {
        if (fpdf_edit.FPDFPageInsertObject(page, path) == 0)
        {
            throw new InvalidDataException("PDFium không chèn được annotation vào trang.");
        }
    }

    private static RgbColor ParseColor(string? value)
    {
        var normalized = PdfAnnotationColor.Normalize(value);
        return new RgbColor(
            uint.Parse(normalized.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            uint.Parse(normalized.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            uint.Parse(normalized.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private static string StandardPdfText(string value)
    {
        var decomposed = value
            .Replace('Đ', 'D')
            .Replace('đ', 'd')
            .Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(character is >= ' ' and <= '~' ? character : '?');
        }

        return builder.ToString();
    }

    private static float ToFloat(double value) => checked((float)value);

    private static Exception CreateLoadException(ulong errorCode) => errorCode switch
    {
        4 => new PdfPasswordRequiredException("PDF cần mật khẩu để xuất bản có chú thích."),
        5 => new UnauthorizedAccessException("Thiết lập bảo mật của PDF không cho phép xuất."),
        _ => new InvalidDataException($"PDFium không mở được PDF nguồn để xuất (mã lỗi {errorCode})."),
    };

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A failed export leaves no user-visible output; a locked temporary
            // file can be cleaned by the OS after the process exits.
        }
    }

    private readonly record struct RgbColor(uint Red, uint Green, uint Blue);
}
