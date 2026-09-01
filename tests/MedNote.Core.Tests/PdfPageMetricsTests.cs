using MedNote.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class PdfPageMetricsTests
{
    [TestMethod]
    [DataRow(612d, 792d, 0.7727272727d)]
    [DataRow(792d, 612d, 1.2941176471d)]
    public void AspectRatio_PreservesPlausiblePageMetrics(double width, double height, double expected)
    {
        var metrics = new PdfPageMetrics(width, height);

        Assert.AreEqual(expected, metrics.AspectRatio, 0.00000001d);
    }

    [TestMethod]
    [DataRow(0d, 792d)]
    [DataRow(60d, 792d)]
    [DataRow(double.NaN, 792d)]
    [DataRow(792d, 0d)]
    public void AspectRatio_FallsBackWhenPdfBoxesAreBroken(double width, double height)
    {
        var metrics = new PdfPageMetrics(width, height);

        Assert.AreEqual(1d / Math.Sqrt(2d), metrics.AspectRatio, 0.000000000001d);
    }
}
