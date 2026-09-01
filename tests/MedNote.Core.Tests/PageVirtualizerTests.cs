using Microsoft.VisualStudio.TestTools.UnitTesting;
using MedNote.Core;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class PageVirtualizerTests
{
    [TestMethod]
    public void VisibleRange_KeepsLargeDocumentsBounded()
    {
        var pages = Enumerable.Range(1, 3_000).ToArray();
        var metrics = PageVirtualizer.CalculateMetrics(pages, new Dictionary<int, double>());

        var range = PageVirtualizer.VisibleRange(metrics, (780d + PageVirtualizer.ContinuousPageGap) * 1_500, 1_080);

        Assert.IsTrue(range.Start > 1_490);
        Assert.IsTrue(range.End - range.Start < 12, $"Expected a small window, got {range.Start}..{range.End}");
        Assert.IsTrue(metrics.TotalHeight > 2_000_000);
    }

    [TestMethod]
    public void AnchorIndex_UsesPageThatContainsInterPageOffset()
    {
        var metrics = PageVirtualizer.CalculateMetrics(
            [1, 2, 3],
            new Dictionary<int, double> { [1] = 700, [2] = 900, [3] = 800 });

        Assert.AreEqual(0, PageVirtualizer.AnchorIndex(metrics, 699));
        Assert.AreEqual(1, PageVirtualizer.AnchorIndex(metrics, 710));
        Assert.AreEqual(2, PageVirtualizer.AnchorIndex(metrics, 5_000));
    }
}
