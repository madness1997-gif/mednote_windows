using System.Text;

namespace MedNote.Core;

public static class NativeNoteTemplates
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static RtfSheetContent FirstAid() => new() { Rtf = FirstAidRtf };

    public static string FirstAidRtf { get; } = BuildFirstAidRtf(0.22d, 30d);

    public static string FirstAidRtfWithLayout(double firstColumnShare, double rowHeightPoints) =>
        BuildFirstAidRtf(firstColumnShare, rowHeightPoints);

    public static string BlankTableRtf(
        int rows,
        int columns,
        double firstColumnShare = 0.5d,
        double rowHeightPoints = 30d)
    {
        rows = Math.Clamp(rows, 1, 12);
        columns = Math.Clamp(columns, 1, 6);
        var edges = CalculateColumnEdges(columns, firstColumnShare);
        var builder = StartRtf();
        for (var row = 0; row < rows; row++)
        {
            builder.Append(@"\trowd\trgaph90\trleft0\trrh")
                .Append(ToTwips(rowHeightPoints));
            foreach (var edge in edges)
            {
                AppendCellDefinition(builder, 0, edge);
            }

            for (var column = 0; column < columns; column++)
            {
                builder.Append(@"\pard\intbl\f0\fs24\cf4\b0 \cell");
            }

            builder.Append(@"\row ");
        }

        return FinishRtf(builder);
    }

    public static string PdfCropBlockRtf(
        byte[] pngBytes,
        uint pixelWidth,
        uint pixelHeight,
        double imageColumnShare,
        double rowHeightPoints,
        string sourceLabel)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLabel);
        if (pngBytes.Length < 8
            || !pngBytes.AsSpan(0, 8).SequenceEqual(PngSignature)
            || pixelWidth == 0
            || pixelHeight == 0)
        {
            throw new ArgumentException("Crop phải là ảnh PNG có kích thước hợp lệ.", nameof(pngBytes));
        }

        var share = NormalizeShare(imageColumnShare);
        var firstEdge = checked((int)Math.Round(10_000d * share));
        var availableWidth = Math.Max(720, firstEdge - 360);
        var goalWidth = availableWidth;
        var goalHeight = checked((int)Math.Round(goalWidth * (double)pixelHeight / pixelWidth));
        var maximumHeight = Math.Max(ToTwips(rowHeightPoints), 5_400);
        if (goalHeight > maximumHeight)
        {
            goalHeight = maximumHeight;
            goalWidth = checked((int)Math.Round(goalHeight * (double)pixelWidth / pixelHeight));
        }

        var builder = StartRtf();
        builder.Append(@"\trowd\trgaph90\trleft0\trrh")
            .Append(Math.Max(ToTwips(rowHeightPoints), goalHeight + 240));
        AppendCellDefinition(builder, 0, firstEdge);
        AppendCellDefinition(builder, 0, 10_000);
        builder.Append(@"\pard\intbl\qc{\pict\pngblip\picw")
            .Append(pixelWidth)
            .Append(@"\pich")
            .Append(pixelHeight)
            .Append(@"\picwgoal")
            .Append(goalWidth)
            .Append(@"\pichgoal")
            .Append(goalHeight)
            .Append(' ');
        AppendHex(builder, pngBytes);
        builder.Append(@"}\cell\pard\intbl\ql\f0\fs24\cf4\b0 \cell\row ");
        builder.Append(@"\pard\f0\fs24\cf1\sa120 ");
        AppendEscaped(builder, sourceLabel);
        builder.Append(@"\par ");
        return FinishRtf(builder);
    }

    private static string BuildFirstAidRtf(double firstColumnShare, double rowHeightPoints)
    {
        var builder = StartRtf();
        var firstEdge = checked((int)Math.Round(10_000d * NormalizeShare(firstColumnShare)));
        AppendTableRow(builder, "FIRST AID", 3, firstEdge, rowHeightPoints);
        AppendTableRow(builder, "CHẨN ĐOÁN", 2, firstEdge, rowHeightPoints);
        AppendTableRow(builder, "CƠ CHẾ", 2, firstEdge, rowHeightPoints);
        AppendTableRow(builder, "XỬ TRÍ", 2, firstEdge, rowHeightPoints);
        AppendTableRow(builder, "CẢNH BÁO", 2, firstEdge, rowHeightPoints);
        return FinishRtf(builder);
    }

    private static void AppendTableRow(
        StringBuilder builder,
        string heading,
        int backgroundColor,
        int firstEdge,
        double rowHeightPoints)
    {
        builder.Append(@"\trowd\trgaph90\trleft0\trrh")
            .Append(ToTwips(rowHeightPoints));
        AppendCellDefinition(builder, backgroundColor, firstEdge);
        AppendCellDefinition(builder, 0, 10000);
        builder.Append(@"\pard\intbl\f0\fs24\cf4\b ");
        AppendEscaped(builder, heading);
        builder.Append(@"\b0\cell\pard\intbl\f0\fs24\cf0\b0 \cell\row ");
    }

    private static StringBuilder StartRtf() => new(
        @"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}{\colortbl;\red14\green107\blue112;\red255\green239\blue153;\red245\green183\blue177;\red24\green50\blue74;}\viewkind4\uc1\pard\f0\fs24 ");

    private static string FinishRtf(StringBuilder builder)
    {
        builder.Append(@"\pard\f0\fs24\sa120 }");
        return builder.ToString();
    }

    private static IReadOnlyList<int> CalculateColumnEdges(int columns, double firstColumnShare)
    {
        if (columns == 1)
        {
            return [10_000];
        }

        var firstEdge = checked((int)Math.Round(10_000d * NormalizeShare(firstColumnShare)));
        var remaining = 10_000 - firstEdge;
        return Enumerable.Range(0, columns)
            .Select(index => index == 0
                ? firstEdge
                : checked(firstEdge + (int)Math.Round(remaining * index / (double)(columns - 1))))
            .ToArray();
    }

    private static double NormalizeShare(double value) =>
        Math.Clamp(double.IsFinite(value) ? value : 0.5d, 0.2d, 0.8d);

    private static int ToTwips(double points) =>
        checked((int)Math.Round(Math.Clamp(double.IsFinite(points) ? points : 30d, 18d, 96d) * 20d));

    private static void AppendCellDefinition(StringBuilder builder, int backgroundColor, int rightEdge)
    {
        builder.Append(@"\clcbpat").Append(backgroundColor)
            .Append(@"\clbrdrt\brdrs\brdrw10\brdrcf1")
            .Append(@"\clbrdrl\brdrs\brdrw10\brdrcf1")
            .Append(@"\clbrdrb\brdrs\brdrw10\brdrcf1")
            .Append(@"\clbrdrr\brdrs\brdrw10\brdrcf1")
            .Append(@"\cellx")
            .Append(rightEdge);
    }

    private static void AppendEscaped(StringBuilder builder, string value)
    {
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\':
                case '{':
                case '}':
                    builder.Append('\\').Append(character);
                    break;
                case '\r':
                    break;
                case '\n':
                    builder.Append(@"\line ");
                    break;
                case <= '\x7f':
                    builder.Append(character);
                    break;
                default:
                    builder.Append(@"\u").Append(unchecked((short)character)).Append('?');
                    break;
            }
        }
    }

    private static void AppendHex(StringBuilder builder, ReadOnlySpan<byte> bytes)
    {
        const string digits = "0123456789abcdef";
        for (var index = 0; index < bytes.Length; index++)
        {
            var value = bytes[index];
            builder.Append(digits[value >> 4]).Append(digits[value & 0xf]);
            if ((index + 1) % 64 == 0)
            {
                builder.Append('\n');
            }
        }
    }
}
