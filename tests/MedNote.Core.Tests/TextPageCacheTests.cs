using MedNote.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class TextPageCacheTests
{
    [TestMethod]
    public void Store_EvictsLeastRecentlyUsedPage()
    {
        var cache = new TextPageCache(330);
        cache.Store(Page(0));
        cache.Store(Page(1));
        Assert.IsTrue(cache.TryGet(0, out _));

        cache.Store(Page(2));

        CollectionAssert.AreEqual(new[] { 0, 2 }, cache.Snapshot().Entries.Select(entry => entry.PageIndex).ToArray());
        Assert.IsFalse(cache.TryGet(1, out _));
    }

    [TestMethod]
    public void Store_DoesNotCachePageLargerThanBudget()
    {
        var cache = new TextPageCache(100);

        cache.Store(new PdfTextPage(4, new string('x', 100)));

        Assert.AreEqual(0, cache.Snapshot().Entries.Count);
        Assert.IsFalse(cache.TryGet(4, out _));
    }

    private static PdfTextPage Page(int index) => new(index, new string((char)('a' + index), 40));
}
