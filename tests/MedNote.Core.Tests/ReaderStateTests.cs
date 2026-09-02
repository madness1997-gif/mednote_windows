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

    [TestMethod]
    public void ContinuousAnchorCorrection_AccountsForContainerTopAndWithinPageRatio()
    {
        Assert.AreEqual(250d, ReaderMath.ContinuousAnchorCorrection(-150d, 1_000d, 0.4d), 0.000_001d);
        Assert.AreEqual(-150d, ReaderMath.ContinuousAnchorCorrection(-150d, 1_000d, -1d), 0.000_001d);
    }

    [TestMethod]
    [DataRow(-91, 270)]
    [DataRow(44, 0)]
    [DataRow(45, 0)]
    [DataRow(225, 180)]
    [DataRow(450, 90)]
    public void NormalizeRotation_SnapsToClockwiseQuarterTurns(int rotation, int expected)
    {
        Assert.AreEqual(expected, ReaderMath.NormalizeRotation(rotation));
    }
}
