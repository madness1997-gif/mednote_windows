using System.Text;

namespace MedNote.Core;

public static class NativeNoteTemplates
{
    public static RtfSheetContent FirstAid() => new() { Rtf = FirstAidRtf };

    public static string FirstAidRtf { get; } = BuildFirstAidRtf();

    private static string BuildFirstAidRtf()
    {
        var builder = new StringBuilder(
            @"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}{\colortbl;\red14\green107\blue112;\red255\green239\blue153;\red245\green183\blue177;\red24\green50\blue74;}\viewkind4\uc1\pard\f0\fs24 ");
        AppendTableRow(builder, "FIRST AID", 3);
        AppendTableRow(builder, "CHẨN ĐOÁN", 2);
        AppendTableRow(builder, "CƠ CHẾ", 2);
        AppendTableRow(builder, "XỬ TRÍ", 2);
        AppendTableRow(builder, "CẢNH BÁO", 2);
        builder.Append(@"\pard\f0\fs24\sa120 ");
        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendTableRow(StringBuilder builder, string heading, int backgroundColor)
    {
        builder.Append(@"\trowd\trgaph90\trleft0");
        AppendCellDefinition(builder, backgroundColor, 2200);
        AppendCellDefinition(builder, 0, 10000);
        builder.Append(@"\pard\intbl\f0\fs24\cf4\b ");
        AppendEscaped(builder, heading);
        builder.Append(@"\b0\cell\pard\intbl\f0\fs24\cf0\b0 \cell\row ");
    }

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
}
