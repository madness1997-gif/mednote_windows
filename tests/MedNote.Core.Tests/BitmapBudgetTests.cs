using Microsoft.VisualStudio.TestTools.UnitTesting;
using MedNote.Core;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class BitmapBudgetTests
{
    [TestMethod]
    public void Report_EvictsLeastRecentlyUsedUnpinnedBitmap()
    {
        var evicted = new List<string>();
        var budget = new BitmapBudget<string>(100);
        budget.Report("page-1", 40, () => evicted.Add("page-1"));
        budget.Report("page-2", 40, () => evicted.Add("page-2"));
        budget.Touch("page-1");
        budget.Report("page-3", 40, () => evicted.Add("page-3"));

        CollectionAssert.AreEqual(new[] { "page-2" }, evicted);
        Assert.AreEqual(80L, budget.Snapshot().TotalBytes);
    }

    [TestMethod]
    public void Report_PreservesVisiblePageEvenWhenBudgetIsTight()
    {
        var pinned = true;
        var evicted = new List<string>();
        var budget = new BitmapBudget<string>(60);
        budget.Report("visible", 50, () => evicted.Add("visible"), () => pinned);
        budget.Report("next", 50, () => evicted.Add("next"));

        Assert.IsFalse(evicted.Contains("visible"));
        Assert.AreEqual(100L, budget.Snapshot().TotalBytes, "Pinned and newly rendered pages may temporarily exceed the budget");
    }
}
