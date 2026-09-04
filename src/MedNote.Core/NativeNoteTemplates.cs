using System.Text;

namespace MedNote.Core;

public static class NativeNoteTemplates
{
    private const double FirstAidLabelShare = 0.22d;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static RtfSheetContent FirstAid() => new() { Rtf = FirstAidRtf };

    public static string FirstAidRtf { get; } = BuildFirstAidRtf(0.25d, 36d);

    public static string FirstAidRtfWithLayout(double imageColumnShare, double rowHeightPoints) =>
        BuildFirstAidRtf(imageColumnShare, rowHeightPoints);

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
            builder.Append(@"\trowd\trgaph90\trleft0\trftsWidth2\trwWidth5000\trrh")
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

        var imageShare = NormalizeImageShare(imageColumnShare);
        var labelEdge = checked((int)Math.Round(10_000d * FirstAidLabelShare));
        var imageEdge = checked((int)Math.Round(10_000d * (1d - imageShare)));
        var availableWidth = Math.Max(720, 10_000 - imageEdge - 360);
        var goalWidth = availableWidth;
        var goalHeight = checked((int)Math.Round(goalWidth * (double)pixelHeight / pixelWidth));
        var maximumHeight = Math.Max(ToTwips(rowHeightPoints), 5_400);
        if (goalHeight > maximumHeight)
        {
            goalHeight = maximumHeight;
            goalWidth = checked((int)Math.Round(goalHeight * (double)pixelWidth / pixelHeight));
        }

        var builder = StartRtf();
        builder.Append(@"\trowd\trgaph90\trleft0\trftsWidth2\trwWidth5000\trrh")
            .Append(Math.Max(ToTwips(rowHeightPoints), goalHeight + 240));
        AppendCellDefinition(builder, 2, labelEdge);
        AppendCellDefinition(builder, 0, imageEdge);
        AppendCellDefinition(builder, 0, 10_000);
        builder.Append(@"\pard\intbl\ql\f0\fs24\cf4\b NGU")
            .Append(@"\u7890?N PDF\b0\cell\pard\intbl\ql\f0\fs24\cf4\b0 ");
        AppendEscaped(builder, sourceLabel);
        builder.Append(@"\cell\pard\intbl\qc{\pict\pngblip\picw")
            .Append(pixelWidth)
            .Append(@"\pich")
            .Append(pixelHeight)
            .Append(@"\picwgoal")
            .Append(goalWidth)
            .Append(@"\pichgoal")
            .Append(goalHeight)
            .Append(' ');
        AppendHex(builder, pngBytes);
        builder.Append(@"}\cell\row ");
        return FinishRtf(builder);
    }

    private static string BuildFirstAidRtf(double imageColumnShare, double rowHeightPoints)
    {
        var builder = StartRtf();
        var labelEdge = checked((int)Math.Round(10_000d * FirstAidLabelShare));
        var imageEdge = checked((int)Math.Round(10_000d * (1d - NormalizeImageShare(imageColumnShare))));
        builder.Append(@"\trowd\trgaph90\trleft0\trftsWidth2\trwWidth5000\trrh")
            .Append(ToTwips(rowHeightPoints));
        AppendCellDefinition(builder, 3, labelEdge);
        AppendCellDefinition(builder, 0, imageEdge);
        AppendCellDefinition(builder, 0, 10_000);
        builder.Append(@"\pard\intbl\f0\fs24\cf4\b FIRST AID\b0\cell")
            .Append(@"\pard\intbl\f0\fs24\cf0\b0 \cell")
            .Append(@"\pard\intbl\f0\fs24\cf0\b0 \cell\row ");
        return FinishRtf(builder);
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

    private static double NormalizeImageShare(double value) =>
        Math.Clamp(double.IsFinite(value) ? value : 0.25d, 0.2d, 0.45d);

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
