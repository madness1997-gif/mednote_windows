using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MedNote.Core.Compatibility.WebV6;

/// <summary>
/// Conservative readable-text projection used before RichEdit produces the
/// native RTF document. Opaque-only non-empty structures fail instead of
/// silently replacing a web Sheet with blank content.
/// </summary>
public static partial class WebV6TextProjection
{
    private static readonly Dictionary<string, string[]> RicherFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["body"] = ["bodyHtml"],
        ["caption"] = ["captionHtml"],
        ["label"] = ["labelHtml"],
        ["rows"] = ["rowsHtml"],
        ["steps"] = ["stepsHtml"],
        ["text"] = ["textHtml", "richText"],
        ["title"] = ["titleHtml"],
    };

    private static readonly HashSet<string> TextFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "body",
        "bodyHtml",
        "caption",
        "captionHtml",
        "content",
        "html",
        "label",
        "labelHtml",
        "richText",
        "rows",
        "rowsHtml",
        "steps",
        "stepsHtml",
        "text",
        "textHtml",
        "title",
        "titleHtml",
    };

    public static string Project(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("SheetContent web v6 phải là object trước khi chuyển sang RTF.");
        }

        RejectUnsupportedVisualContent(content);
        var lines = new List<string>();
        Collect(content, propertyName: null, lines);
        var result = string.Join(
            Environment.NewLine,
            lines.Select(Normalize).Where(line => line.Length > 0));
        if (result.Length == 0 && content.EnumerateObject().Any() && !HasKnownEmptyContentShape(content))
        {
            throw new InvalidDataException("SheetContent web v6 có dữ liệu nhưng không có nội dung chữ chuyển đổi an toàn.");
        }

        return result;
    }

    private static void Collect(JsonElement value, string? propertyName, ICollection<string> lines)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var richerProperties = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in value.EnumerateObject())
                {
                    richerProperties[property.Name] = property.Value;
                }

                foreach (var property in value.EnumerateObject())
                {
                    if (RicherFields.TryGetValue(property.Name, out var alternatives)
                        && alternatives.Any(name => richerProperties.TryGetValue(name, out var richer) && HasText(richer)))
                    {
                        continue;
                    }

                    Collect(property.Value, property.Name, lines);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    Collect(item, propertyName, lines);
                }

                break;
            case JsonValueKind.String when propertyName is not null && TextFields.Contains(propertyName):
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text) && !text.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    lines.Add(HtmlToText(text));
                }

                break;
        }
    }

    private static bool HasKnownEmptyContentShape(JsonElement content) =>
        content.TryGetProperty("body", out _)
        || content.TryGetProperty("bodyHtml", out _)
        || content.TryGetProperty("firstAid", out _)
        || content.TryGetProperty("firstAidBlocks", out _);

    private static bool HasText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
        JsonValueKind.Array => value.EnumerateArray().Any(HasText),
        _ => false,
    };

    private static void RejectUnsupportedVisualContent(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                RejectUnsupportedVisualContent(item);
            }

            return;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in value.EnumerateObject())
        {
            var propertyValue = property.Value;
            if (propertyValue.ValueKind == JsonValueKind.String
                && propertyValue.GetString() is { } stringValue
                && (stringValue.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
                    || stringValue.Contains("<img", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("SheetContent web v6 có ảnh nhúng chưa thể chuyển an toàn sang RTF.");
            }

            if (property.NameEquals("strokes")
                && propertyValue.ValueKind == JsonValueKind.Array
                && propertyValue.GetArrayLength() > 0)
            {
                throw new InvalidDataException("SheetContent web v6 có nét vẽ chưa thể chuyển an toàn sang RTF.");
            }

            if ((property.NameEquals("assetId")
                    || property.NameEquals("imageAssetId")
                    || property.NameEquals("imageObjectId")
                    || property.NameEquals("imageName"))
                && propertyValue.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(propertyValue.GetString()))
            {
                throw new InvalidDataException("SheetContent web v6 có ảnh chưa thể chuyển an toàn sang RTF.");
            }

            if (property.NameEquals("kind")
                && propertyValue.ValueKind == JsonValueKind.String
                && string.Equals(propertyValue.GetString(), "image", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("SheetContent web v6 có excerpt ảnh chưa thể chuyển an toàn sang RTF.");
            }

            RejectUnsupportedVisualContent(propertyValue);
        }
    }

    private static string HtmlToText(string value)
    {
        value = HtmlBreakRegex().Replace(value, Environment.NewLine);
        value = HtmlTagRegex().Replace(value, string.Empty);
        return WebUtility.HtmlDecode(value);
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWhitespace = false;
        foreach (var character in value.Trim())
        {
            if (character is '\r' or '\n')
            {
                if (builder.Length > 0 && builder[^1] != '\n')
                {
                    builder.Append('\n');
                }

                previousWhitespace = false;
            }
            else if (char.IsWhiteSpace(character))
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                    previousWhitespace = true;
                }
            }
            else
            {
                builder.Append(character);
                previousWhitespace = false;
            }
        }

        return builder.ToString().Trim();
    }

    [GeneratedRegex(@"<\s*(br\s*/?|/p|/div|/li|/tr|/td|/th)\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlBreakRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();
}
