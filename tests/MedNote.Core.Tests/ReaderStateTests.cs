using Microsoft.VisualStudio.TestTools.UnitTesting;
using MedNote.Core;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class ReaderStateTests
{
    [TestMethod]
    public void Normalize_ClampsAndDeduplicatesReaderFields()
    {
        var state = new ReaderState
        {
            Page = 9_999,
            Zoom = 8,
            Rotation = -91,
            Bookmarks = [7, 2, 7, 0, 999],
        };

        var normalized = state.Normalize(12);

        Assert.AreEqual(12, normalized.Page);
        Assert.AreEqual(ReaderMath.MaximumZoom, normalized.Zoom);
        Assert.AreEqual(270, normalized.Rotation);
        CollectionAssert.AreEqual(new[] { 2, 7 }, normalized.Bookmarks);
    }

    [TestMethod]
    public void Position_NormalizesAnchorWithoutLosingWithinPageOffset()
    {
        var position = new ReaderPosition
        {
            AnchorPage = -2,
            PageOffsetRatio = 0.42,
            HorizontalOffset = -100,
        }.Normalize(3_000);

        Assert.AreEqual(1, position.AnchorPage);
        Assert.AreEqual(0.42, position.PageOffsetRatio, 0.000_001);
        Assert.AreEqual(0d, position.HorizontalOffset);
    }
}
