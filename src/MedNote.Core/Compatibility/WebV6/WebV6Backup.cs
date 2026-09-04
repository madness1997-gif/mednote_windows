using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedNote.Core;

namespace MedNote.Core.Compatibility.WebV6;

public sealed record WebDriveBackupV2
{
    [JsonRequired]
    public string Format { get; init; } = string.Empty;

    [JsonRequired]
    public int SchemaVersion { get; init; }

    [JsonRequired]
    public long ExportedAt { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SheetHashAlgorithm { get; init; }

    [JsonRequired]
    public Dictionary<string, string> SheetContentHashes { get; init; } = [];

    [JsonRequired]
    public WebLibraryV6 Library { get; init; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public static class WebV6Backup
{
    private static readonly JsonSerializerOptions CompactJson = JsonDefaults.Create();
    private static readonly JsonSerializerOptions JavascriptStringJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static WebDriveBackupV2 Create(WebLibraryV6 library, long? exportedAt = null)
    {
        var snapshot = JsonClone(library);
        WebV6LibraryValidator.AssertValid(snapshot);
        return new WebDriveBackupV2
        {
            Format = WebV6Schema.DriveBackupFormat,
            SchemaVersion = WebV6Schema.Version,
            ExportedAt = exportedAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SheetHashAlgorithm = WebV6Schema.SheetHashAlgorithm,
            SheetContentHashes = HashesFor(snapshot),
            Library = snapshot,
        };
    }

    public static WebLibraryV6 Parse(ReadOnlySpan<byte> payload)
    {
        WebDriveBackupV2 backup;
        try
        {
            backup = JsonSerializer.Deserialize<WebDriveBackupV2>(payload, CompactJson)
                ?? throw new InvalidDataException("Bản lưu Drive không hợp lệ.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Bản lưu Drive không phải JSON v2 hợp lệ.", exception);
        }

        return Parse(backup);
    }

    public static WebLibraryV6 Parse(WebDriveBackupV2 backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        if (backup.Library is null
            || backup.SheetContentHashes is null
            || backup.Format != WebV6Schema.DriveBackupFormat
            || backup.SchemaVersion != WebV6Schema.Version
            || backup.Library.Version != WebV6Schema.Version)
        {
            throw new InvalidDataException("Bản lưu Drive không phải manifest v2 dùng schema v6.");
        }

        var library = JsonClone(backup.Library);
        WebV6LibraryValidator.AssertValid(library);
        var actual = HashesFor(library);
        var expected = backup.SheetContentHashes;
        var sameKeys = actual.Count == expected.Count && actual.Keys.All(expected.ContainsKey);
        var mismatch = !sameKeys || actual.Any(item => !expected.TryGetValue(item.Key, out var hash) || hash != item.Value);
        if (mismatch)
        {
            var legacyHashesAreStructurallyComplete = backup.SheetHashAlgorithm is null
                && sameKeys
                && backup.ExportedAt > 0
                && expected.Values.All(hash => !string.IsNullOrEmpty(hash));
            if (!legacyHashesAreStructurallyComplete)
            {
                throw new InvalidDataException("Hash nội dung Sheet trong bản lưu Drive không khớp.");
            }
        }

        return library;
    }

    public static Dictionary<string, string> HashesFor(WebLibraryV6 library) =>
        library.Notes.Sheets.ToDictionary(
            sheet => sheet.Id,
            sheet => ContentHash(library.SheetContents[sheet.Id]),
            StringComparer.Ordinal);

    public static string ContentHash(JsonElement content) => StableHash(StableStringify(content));

    public static string StableHash(string value)
    {
        var hash = 2166136261u;
        foreach (var codeUnit in value)
        {
            hash ^= codeUnit;
            hash = unchecked(hash * 16777619u);
        }

        return ToBase36(hash);
    }

    public static string StableStringify(JsonElement value)
    {
        var builder = new StringBuilder();
        AppendStableJson(builder, value);
        return builder.ToString();
    }

    public static void VerifyRoundTrip(WebLibraryV6 expected, WebLibraryV6 actual)
    {
        WebV6LibraryValidator.AssertValid(expected);
        WebV6LibraryValidator.AssertValid(actual);
        var expectedJson = StableStringify(JsonSerializer.SerializeToElement(expected, CompactJson));
        var actualJson = StableStringify(JsonSerializer.SerializeToElement(actual, CompactJson));
        if (!string.Equals(expectedJson, actualJson, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Round-trip không giữ nguyên dữ liệu thư viện v6.");
        }
    }

    private static T JsonClone<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, CompactJson);
        return JsonSerializer.Deserialize<T>(bytes, CompactJson)
            ?? throw new InvalidDataException("Không thể sao chép dữ liệu JSON v6.");
    }

    private static void AppendStableJson(StringBuilder builder, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                var properties = value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).ToList();
                for (var index = 0; index < properties.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }

                    builder.Append(JsonSerializer.Serialize(properties[index].Name, JavascriptStringJson));
                    builder.Append(':');
                    AppendStableJson(builder, properties[index].Value);
                }

                builder.Append('}');
                break;
            case JsonValueKind.Array:
                builder.Append('[');
                var first = true;
                foreach (var item in value.EnumerateArray())
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    AppendStableJson(builder, item);
                    first = false;
                }

                builder.Append(']');
                break;
            case JsonValueKind.String:
                builder.Append(JsonSerializer.Serialize(value.GetString(), JavascriptStringJson));
                break;
            case JsonValueKind.Number:
                builder.Append(FormatJavascriptNumber(value.GetDouble()));

                break;
            case JsonValueKind.True:
                builder.Append("true");
                break;
            case JsonValueKind.False:
                builder.Append("false");
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                builder.Append("null");
                break;
            default:
                throw new InvalidDataException($"Kiểu JSON {value.ValueKind} không được hỗ trợ.");
        }
    }

    private static string ToBase36(uint value)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        if (value == 0)
        {
            return "0";
        }

        Span<char> buffer = stackalloc char[7];
        var index = buffer.Length;
        while (value > 0)
        {
            buffer[--index] = digits[(int)(value % 36)];
            value /= 36;
        }

        return new string(buffer[index..]);
    }

    private static string FormatJavascriptNumber(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidDataException("SheetContent chứa số không hữu hạn.");
        }

        if (value == 0d)
        {
            return "0";
        }

        var negative = value < 0d;
        var roundTrip = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        var exponentMarker = roundTrip.IndexOf('E');
        if (exponentMarker < 0)
        {
            exponentMarker = roundTrip.IndexOf('e');
        }

        var mantissa = exponentMarker < 0 ? roundTrip : roundTrip[..exponentMarker];
        var exponent = exponentMarker < 0
            ? 0
            : int.Parse(roundTrip[(exponentMarker + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var decimalPoint = mantissa.IndexOf('.');
        var decimalPosition = (decimalPoint < 0 ? mantissa.Length : decimalPoint) + exponent;
        var digits = mantissa.Replace(".", string.Empty, StringComparison.Ordinal);
        var leadingZeroCount = 0;
        while (leadingZeroCount < digits.Length && digits[leadingZeroCount] == '0')
        {
            leadingZeroCount++;
        }

        digits = digits[leadingZeroCount..].TrimEnd('0');
        decimalPosition -= leadingZeroCount;
        if (digits.Length == 0)
        {
            return "0";
        }

        string formatted;
        if (decimalPosition > 0 && decimalPosition <= 21)
        {
            formatted = decimalPosition >= digits.Length
                ? digits + new string('0', decimalPosition - digits.Length)
                : $"{digits[..decimalPosition]}.{digits[decimalPosition..]}";
        }
        else if (decimalPosition <= 0 && decimalPosition > -6)
        {
            formatted = $"0.{new string('0', -decimalPosition)}{digits}";
        }
        else
        {
            var scientificExponent = decimalPosition - 1;
            var significand = digits.Length == 1 ? digits : $"{digits[0]}.{digits[1..]}";
            formatted = $"{significand}e{(scientificExponent >= 0 ? "+" : string.Empty)}{scientificExponent.ToString(CultureInfo.InvariantCulture)}";
        }

        return negative ? $"-{formatted}" : formatted;
    }
}
