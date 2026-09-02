using MedNote.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class PdfPageCoordinateMapperTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(90)]
    [DataRow(180)]
    [DataRow(270)]
    public void DisplayToPage_RoundTripsRectangleCenter(int rotation)
    {
        var page = new PdfPageMetrics(600d, 800d);
        var displayWidth = rotation is 90 or 270 ? 800d : 600d;
        var displayHeight = rotation is 90 or 270 ? 600d : 800d;
        var source = new PdfPageRect(120d, 240d, 300d, 400d);

        var displayed = PdfPageCoordinateMapper.PageToDisplay(
            source,
            page,
            displayWidth,
            displayHeight,
            rotation);
        var mappedBack = PdfPageCoordinateMapper.DisplayToPage(
            new PdfPagePoint(
                displayed.Left + displayed.Width / 2d,
                displayed.Top + displayed.Height / 2d),
            page,
            displayWidth,
            displayHeight,
            rotation);

        Assert.AreEqual(210d, mappedBack.X, 0.001d);
        Assert.AreEqual(320d, mappedBack.Y, 0.001d);
    }

    [TestMethod]
    public void PageToDisplay_RotatesClockwise()
    {
        var displayed = PdfPageCoordinateMapper.PageToDisplay(
            new PdfPageRect(0d, 0d, 100d, 200d),
            new PdfPageMetrics(600d, 800d),
            800d,
            600d,
            90);

        Assert.AreEqual(600d, displayed.Left, 0.001d);
        Assert.AreEqual(0d, displayed.Top, 0.001d);
        Assert.AreEqual(800d, displayed.Right, 0.001d);
        Assert.AreEqual(100d, displayed.Bottom, 0.001d);
    }
}
