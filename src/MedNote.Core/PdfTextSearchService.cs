using System.Globalization;
using System.Runtime.CompilerServices;

namespace MedNote.Core;

public sealed record PdfSearchOptions
{
    public bool MatchCase { get; init; }

    public bool WholeWords { get; init; }

    public int MaxResults { get; init; } = 500;

    public int SnippetLength { get; init; } = 120;

    public PdfSearchOptions Normalize() => this with
    {
        MaxResults = Math.Clamp(MaxResults, 1, 10_000),
        SnippetLength = Math.Clamp(SnippetLength, 40, 500),
    };
}

public sealed record PdfSearchMatch(int PageIndex, int StartIndex, int Length, string Snippet)
{
    public int PageNumber => PageIndex + 1;
}

public readonly record struct PdfSearchProgress(int ScannedPages, int TotalPages, int MatchCount);

public sealed class PdfTextSearchService
{
    private readonly IPdfTextProvider _source;
    private readonly TextPageCache _cache;

    public PdfTextSearchService(IPdfTextProvider source, TextPageCache? cache = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _cache = cache ?? new TextPageCache();
    }

    public TextPageCache Cache => _cache;

    public async IAsyncEnumerable<PdfSearchMatch> SearchAsync(
        string query,
        PdfSearchOptions? options = null,
        IProgress<PdfSearchProgress>? progress = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query) || _source.PageCount <= 0)
        {
            yield break;
        }

        var normalized = (options ?? new PdfSearchOptions()).Normalize();
        var comparison = normalized.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var matchCount = 0;

        for (var pageIndex = 0; pageIndex < _source.PageCount; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await GetPageAsync(pageIndex, cancellationToken);
            foreach (var startIndex in FindMatches(page.Text ?? string.Empty, query, comparison, normalized.WholeWords))
            {
                cancellationToken.ThrowIfCancellationRequested();
                matchCount++;
                yield return new PdfSearchMatch(
                    pageIndex,
                    startIndex,
                    query.Length,
                    CreateSnippet(page.Text ?? string.Empty, startIndex, query.Length, normalized.SnippetLength));

                if (matchCount >= normalized.MaxResults)
                {
                    progress?.Report(new PdfSearchProgress(pageIndex + 1, _source.PageCount, matchCount));
                    yield break;
                }
            }

            progress?.Report(new PdfSearchProgress(pageIndex + 1, _source.PageCount, matchCount));
        }
    }

    private async ValueTask<PdfTextPage> GetPageAsync(int pageIndex, CancellationToken cancellationToken)
    {
        if (_cache.TryGet(pageIndex, out var cached) && cached is not null)
        {
            return cached;
        }

        var page = await _source.GetTextPageAsync(pageIndex, cancellationToken);
        if (page.PageIndex != pageIndex)
        {
            page = page with { PageIndex = pageIndex };
        }

        _cache.Store(page);
        return page;
    }

    private static IEnumerable<int> FindMatches(
        string text,
        string query,
        StringComparison comparison,
        bool wholeWords)
    {
        var searchFrom = 0;
        while (searchFrom <= text.Length - query.Length)
        {
            var index = text.IndexOf(query, searchFrom, comparison);
            if (index < 0)
            {
                yield break;
            }

            if (!wholeWords || IsWholeWord(text, index, query.Length))
            {
                yield return index;
            }

            searchFrom = index + Math.Max(1, query.Length);
        }
    }

    private static bool IsWholeWord(string text, int startIndex, int length)
    {
        var leftIsWord = startIndex > 0 && IsWordCharacter(text[startIndex - 1]);
        var endIndex = startIndex + length;
        var rightIsWord = endIndex < text.Length && IsWordCharacter(text[endIndex]);
        return !leftIsWord && !rightIsWord;
    }

    private static bool IsWordCharacter(char character)
    {
        if (char.IsLetterOrDigit(character) || character == '_')
        {
            return true;
        }

        var category = char.GetUnicodeCategory(character);
        return category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.ConnectorPunctuation;
    }

    private static string CreateSnippet(string text, int startIndex, int length, int maximumLength)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var contextLength = Math.Max(0, maximumLength - length);
        var left = Math.Max(0, startIndex - contextLength / 2);
        var right = Math.Min(text.Length, startIndex + length + contextLength / 2);
        if (right - left > maximumLength)
        {
            right = left + maximumLength;
        }

        var builder = new System.Text.StringBuilder(right - left + 2);
        var previousWasWhitespace = false;
        for (var index = left; index < right; index++)
        {
            var character = text[index];
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }
            }
            else
            {
                builder.Append(character);
                previousWasWhitespace = false;
            }
        }

        return builder.ToString().Trim();
    }
}
