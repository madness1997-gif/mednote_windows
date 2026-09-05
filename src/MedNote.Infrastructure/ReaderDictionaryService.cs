using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MedNote.Infrastructure;

public sealed record ReaderDictionaryResult(string? Translation, IReadOnlyList<string> Alternatives,
    string? Phonetic, string? AudioUrl, IReadOnlyList<string> Definitions, string? Error);

/// <summary>Explicit, cancellable lookup using the same public providers as the web Reader.</summary>
public sealed class ReaderDictionaryService(HttpClient http)
{
    public async Task<ReaderDictionaryResult> LookupAsync(string source, CancellationToken cancellationToken)
    {
        source = Regex.Replace(source.Trim(), @"\s+", " ");
        if (source.Length == 0 || source.Length > 500)
            throw new ArgumentException("Chọn đoạn chữ từ 1 đến 500 ký tự để dịch.");

        string? translation = null, error = null, phonetic = null, audio = null;
        var alternatives = new List<string>();
        var definitions = new List<string>();
        try
        {
            using var response = await http.GetAsync(
                $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(source)}&langpair=en%7Cvi", cancellationToken);
            response.EnsureSuccessStatusCode();
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = payload.RootElement;
            if (root.TryGetProperty("responseStatus", out var status) && status.ToString() != "200")
                throw new InvalidOperationException("Dịch vụ dịch đang giới hạn yêu cầu. Hãy thử lại sau.");
            translation = root.TryGetProperty("responseData", out var data) ? String(data, "translatedText") : null;
            translation = WebUtility.HtmlDecode(translation);
            if (string.Equals(translation, source, StringComparison.OrdinalIgnoreCase)) translation = null;
            if (root.TryGetProperty("matches", out var matches) && matches.ValueKind == JsonValueKind.Array)
                foreach (var match in matches.EnumerateArray())
                {
                    var value = WebUtility.HtmlDecode(String(match, "translation"));
                    if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value, source, StringComparison.OrdinalIgnoreCase)
                        && value != translation && !alternatives.Contains(value) && alternatives.Count < 3)
                        alternatives.Add(value);
                }
            if (string.IsNullOrWhiteSpace(translation) && alternatives.Count > 0)
            {
                translation = alternatives[0];
                alternatives.RemoveAt(0);
            }
            if (string.IsNullOrWhiteSpace(translation)) error = "Chưa tìm thấy gợi ý dịch phù hợp.";
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException)
        {
            error = "Chưa lấy được bản dịch. Hãy kiểm tra kết nối rồi thử lại.";
        }

        if (Regex.IsMatch(source, @"^[A-Za-z][A-Za-z'’-]*$"))
        {
            try
            {
                using var response = await http.GetAsync(
                    $"https://api.dictionaryapi.dev/api/v2/entries/en/{Uri.EscapeDataString(source.ToLowerInvariant())}", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                    if (payload.RootElement.ValueKind == JsonValueKind.Array && payload.RootElement.GetArrayLength() > 0)
                    {
                        var entry = payload.RootElement[0];
                        phonetic = String(entry, "phonetic");
                        if (entry.TryGetProperty("phonetics", out var phonetics) && phonetics.ValueKind == JsonValueKind.Array)
                            foreach (var item in phonetics.EnumerateArray())
                            {
                                phonetic ??= String(item, "text");
                                var candidate = String(item, "audio");
                                if (audio is null && Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                                    && uri.Scheme == "https" && uri.Host == "api.dictionaryapi.dev") audio = candidate;
                            }
                        if (entry.TryGetProperty("meanings", out var meanings) && meanings.ValueKind == JsonValueKind.Array)
                            foreach (var meaning in meanings.EnumerateArray().Take(3))
                                if (meaning.TryGetProperty("definitions", out var items) && items.ValueKind == JsonValueKind.Array)
                                    foreach (var item in items.EnumerateArray().Take(2))
                                        if (String(item, "definition") is { } definition)
                                            definitions.Add($"{String(meaning, "partOfSpeech")}: {definition}");
                    }
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException)
            {
                // Dictionary failure must not discard a successful translation.
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(translation, alternatives, phonetic, audio, definitions, error);
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!.Trim() : null;
}
