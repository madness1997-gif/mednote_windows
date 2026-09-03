using System.Text.Json;
using System.Text.Json.Serialization;

namespace MedNote.Core;

public enum PdfAnnotationKind
{
    Highlight,
    AreaHighlight,
    Underline,
    Strikeout,
    Squiggly,
    Ink,
    Note,
    Text,
    Rectangle,
    Ellipse,
    Arrow,
    Stamp,
    Signature,
}

public readonly record struct PdfAnnotationPoint(double X, double Y, double Pressure = 0.5d)
{
    public PdfAnnotationPoint Normalize() => new(
        double.IsFinite(X) ? X : 0d,
        double.IsFinite(Y) ? Y : 0d,
        Math.Clamp(double.IsFinite(Pressure) ? Pressure : 0.5d, 0d, 1d));
}

public readonly record struct PdfAnnotationRect(double X1, double Y1, double X2, double Y2)
{
    public double Left => Math.Min(X1, X2);

    public double Bottom => Math.Min(Y1, Y2);

    public double Right => Math.Max(X1, X2);

    public double Top => Math.Max(Y1, Y2);

    public double Width => Math.Max(0d, Right - Left);

    public double Height => Math.Max(0d, Top - Bottom);

    public PdfAnnotationRect Normalize() => new(
        double.IsFinite(Left) ? Left : 0d,
        double.IsFinite(Bottom) ? Bottom : 0d,
        double.IsFinite(Right) ? Right : 0d,
        double.IsFinite(Top) ? Top : 0d);
}

/// <summary>
/// The native model deliberately uses the same discriminated-union fields as
/// the web v6 <c>PdfAnnotation</c> contract. Fields which do not belong to a
/// particular annotation kind remain null and are omitted from JSON.
/// </summary>
public sealed record PdfAnnotation
{
    public string Id { get; init; } = string.Empty;

    public PdfAnnotationKind Kind { get; init; }

    public int Page { get; init; } = 1;

    public string Color { get; init; } = "#1c2933";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Width { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PdfAnnotationRect>? Rects { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PdfAnnotationRect? Rect { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PdfAnnotationPoint>? Points { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    public long CreatedAt { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];

    public bool IsMarkup => Kind is PdfAnnotationKind.Highlight
        or PdfAnnotationKind.AreaHighlight
        or PdfAnnotationKind.Underline
        or PdfAnnotationKind.Strikeout
        or PdfAnnotationKind.Squiggly;

    public bool IsObject => Kind is PdfAnnotationKind.Note
        or PdfAnnotationKind.Text
        or PdfAnnotationKind.Rectangle
        or PdfAnnotationKind.Ellipse
        or PdfAnnotationKind.Arrow
        or PdfAnnotationKind.Stamp
        or PdfAnnotationKind.Signature;

    public PdfAnnotation Normalize(int pageCount)
    {
        double? normalizedWidth = Width is null
            ? null
            : Math.Clamp(double.IsFinite(Width.Value) ? Width.Value : 1d, 0.1d, 200d);
        return this with
        {
            Id = string.IsNullOrWhiteSpace(Id) ? $"pdf-{Guid.NewGuid():N}" : Id,
            Page = ReaderMath.ClampPage(Page, pageCount),
            Color = PdfAnnotationColor.Normalize(Color),
            Width = normalizedWidth,
            Rects = Rects?.Select(rectangle => rectangle.Normalize()).ToList(),
            Rect = Rect?.Normalize(),
            Points = Points?.Select(point => point.Normalize()).ToList(),
            Text = Text ?? (IsMarkup || IsObject ? string.Empty : null),
            CreatedAt = CreatedAt > 0 ? CreatedAt : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }
}

public static class PdfAnnotationColor
{
    public static string Normalize(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return "#1c2933";
        }

        var value = color.Trim();
        if (value.Length == 4 && value[0] == '#'
            && value.Skip(1).All(Uri.IsHexDigit))
        {
            return $"#{value[1]}{value[1]}{value[2]}{value[2]}{value[3]}{value[3]}".ToLowerInvariant();
        }

        if (value.Length == 7 && value[0] == '#'
            && value.Skip(1).All(Uri.IsHexDigit))
        {
            return value.ToLowerInvariant();
        }

        return "#1c2933";
    }
}

public static class PdfAnnotationJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static bool TryDeserialize(JsonElement element, out PdfAnnotation? annotation)
    {
        annotation = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        try
        {
            annotation = element.Deserialize<PdfAnnotation>(Options);
            return annotation is not null
                && !string.IsNullOrWhiteSpace(annotation.Id)
                && annotation.Page >= 1;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            annotation = null;
            return false;
        }
    }

    public static JsonElement Serialize(PdfAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        return JsonSerializer.SerializeToElement(annotation, Options).Clone();
    }

    public static IReadOnlyList<PdfAnnotation> DeserializeKnown(IEnumerable<JsonElement> annotations) =>
        annotations
            .Select(element => TryDeserialize(element, out var annotation) ? annotation : null)
            .OfType<PdfAnnotation>()
            .ToArray();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = JsonDefaults.Create();
        options.Converters.Insert(0, new PdfAnnotationKindJsonConverter());
        return options;
    }
}

public sealed class PdfAnnotationKindJsonConverter : JsonConverter<PdfAnnotationKind>
{
    public override PdfAnnotationKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value switch
        {
            "highlight" => PdfAnnotationKind.Highlight,
            "area-highlight" => PdfAnnotationKind.AreaHighlight,
            "underline" => PdfAnnotationKind.Underline,
            "strikeout" => PdfAnnotationKind.Strikeout,
            "squiggly" => PdfAnnotationKind.Squiggly,
            "ink" => PdfAnnotationKind.Ink,
            "note" => PdfAnnotationKind.Note,
            "text" => PdfAnnotationKind.Text,
            "rectangle" => PdfAnnotationKind.Rectangle,
            "ellipse" => PdfAnnotationKind.Ellipse,
            "arrow" => PdfAnnotationKind.Arrow,
            "stamp" => PdfAnnotationKind.Stamp,
            "signature" => PdfAnnotationKind.Signature,
            _ => throw new JsonException($"Loại annotation không được native Reader nhận biết: {value}"),
        };
    }

    public override void Write(Utf8JsonWriter writer, PdfAnnotationKind value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            PdfAnnotationKind.Highlight => "highlight",
            PdfAnnotationKind.AreaHighlight => "area-highlight",
            PdfAnnotationKind.Underline => "underline",
            PdfAnnotationKind.Strikeout => "strikeout",
            PdfAnnotationKind.Squiggly => "squiggly",
            PdfAnnotationKind.Ink => "ink",
            PdfAnnotationKind.Note => "note",
            PdfAnnotationKind.Text => "text",
            PdfAnnotationKind.Rectangle => "rectangle",
            PdfAnnotationKind.Ellipse => "ellipse",
            PdfAnnotationKind.Arrow => "arrow",
            PdfAnnotationKind.Stamp => "stamp",
            PdfAnnotationKind.Signature => "signature",
            _ => throw new JsonException($"Loại annotation không hợp lệ: {value}"),
        });
}

public static class PdfAnnotationCoordinateMapper
{
    public static PdfAnnotationPoint DisplayToAnnotation(
        PdfPagePoint displayPoint,
        PdfPageMetrics page,
        double displayWidth,
        double displayHeight,
        int rotation,
        double pressure = 0.5d)
    {
        var point = PdfPageCoordinateMapper.DisplayToPage(
            displayPoint,
            page,
            displayWidth,
            displayHeight,
            rotation);
        return new PdfAnnotationPoint(point.X, page.Height - point.Y, pressure).Normalize();
    }

    public static PdfAnnotationRect DisplayToAnnotation(
        PdfPagePoint first,
        PdfPagePoint second,
        PdfPageMetrics page,
        double displayWidth,
        double displayHeight,
        int rotation)
    {
        var start = DisplayToAnnotation(first, page, displayWidth, displayHeight, rotation);
        var end = DisplayToAnnotation(second, page, displayWidth, displayHeight, rotation);
        return new PdfAnnotationRect(start.X, start.Y, end.X, end.Y).Normalize();
    }

    public static PdfPageRect AnnotationToDisplay(
        PdfAnnotationRect annotation,
        PdfPageMetrics page,
        double displayWidth,
        double displayHeight,
        int rotation)
    {
        annotation = annotation.Normalize();
        var topOrigin = new PdfPageRect(
            annotation.Left,
            page.Height - annotation.Top,
            annotation.Right,
            page.Height - annotation.Bottom);
        return PdfPageCoordinateMapper.PageToDisplay(
            topOrigin,
            page,
            displayWidth,
            displayHeight,
            rotation);
    }

    public static PdfPagePoint AnnotationToDisplay(
        PdfAnnotationPoint annotation,
        PdfPageMetrics page,
        double displayWidth,
        double displayHeight,
        int rotation)
    {
        var display = AnnotationToDisplay(
            new PdfAnnotationRect(annotation.X, annotation.Y, annotation.X, annotation.Y),
            page,
            displayWidth,
            displayHeight,
            rotation);
        return new PdfPagePoint(display.Left, display.Top);
    }
}
