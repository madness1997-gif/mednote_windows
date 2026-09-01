using MedNote.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class PdfTextSearchServiceTests
{
    [TestMethod]
    public async Task SearchAsync_FindsVietnameseTextAcrossPages()
    {
        var source = new FakeTextProvider(
            "Điều trị Đái tháo đường típ 2.",
            "Không có kết quả.",
            "Theo dõi biến chứng đái tháo đường.");
        var service = new PdfTextSearchService(source);

        var matches = await CollectAsync(service.SearchAsync("đái tháo đường"));

        Assert.AreEqual(2, matches.Count);
        CollectionAssert.AreEqual(new[] { 1, 3 }, matches.Select(match => match.PageNumber).ToArray());
        Assert.IsTrue(matches.All(match => match.Snippet.Contains("tháo đường", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task SearchAsync_WholeWordsRejectsSubstring()
    {
        var source = new FakeTextProvider("insulin insulinoma insulin-resistant");
        var service = new PdfTextSearchService(source);
        var options = new PdfSearchOptions { WholeWords = true };

        var matches = await CollectAsync(service.SearchAsync("insulin", options));

        Assert.AreEqual(2, matches.Count, "A hyphen is a word boundary; the insulin in insulinoma is not.");
    }

    [TestMethod]
    public async Task SearchAsync_UsesBoundedCacheOnSecondPass()
    {
        var source = new FakeTextProvider("alpha", "beta", "gamma");
        var service = new PdfTextSearchService(source, new TextPageCache(1_024));

        await CollectAsync(service.SearchAsync("a"));
        await CollectAsync(service.SearchAsync("a"));

        CollectionAssert.AreEqual(new[] { 1, 1, 1 }, source.LoadCounts);
    }

    [TestMethod]
    public async Task SearchAsync_StopsAtConfiguredResultLimit()
    {
        var source = new FakeTextProvider("term term", "term term");
        var service = new PdfTextSearchService(source);

        var matches = await CollectAsync(service.SearchAsync("term", new PdfSearchOptions { MaxResults = 3 }));

        Assert.AreEqual(3, matches.Count);
    }

    [TestMethod]
    public async Task SearchAsync_ObservesCancellationBetweenPages()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new FakeTextProvider("first", "second");
        source.AfterLoad = pageIndex =>
        {
            if (pageIndex == 0)
            {
                cancellation.Cancel();
            }
        };
        var service = new PdfTextSearchService(source);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await CollectAsync(service.SearchAsync("missing", cancellationToken: cancellation.Token));
        });
        Assert.AreEqual(0, source.LoadCounts[1]);
    }

    private static async Task<List<PdfSearchMatch>> CollectAsync(IAsyncEnumerable<PdfSearchMatch> source)
    {
        var matches = new List<PdfSearchMatch>();
        await foreach (var match in source)
        {
            matches.Add(match);
        }

        return matches;
    }

    private sealed class FakeTextProvider : IPdfTextProvider
    {
        private readonly string[] _pages;

        public FakeTextProvider(params string[] pages)
        {
            _pages = pages;
            LoadCounts = new int[pages.Length];
        }

        public int PageCount => _pages.Length;

        public int[] LoadCounts { get; }

        public Action<int>? AfterLoad { get; set; }

        public ValueTask<PdfTextPage> GetTextPageAsync(int pageIndex, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCounts[pageIndex]++;
            var result = new PdfTextPage(pageIndex, _pages[pageIndex]);
            AfterLoad?.Invoke(pageIndex);
            return ValueTask.FromResult(result);
        }

        public ValueTask<IReadOnlyList<PdfPageRect>> GetTextBoundsAsync(
            int pageIndex,
            int startIndex,
            int length,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<PdfPageRect>>(Array.Empty<PdfPageRect>());
    }
}
