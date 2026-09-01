using MedNote.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class PdfContentContractsTests
{
    [TestMethod]
    public void Destination_NormalizeClampsPageAndZoom()
    {
        var destination = new PdfDestination(99, 12d, 34d, 20d).Normalize(10);

        Assert.AreEqual(9, destination.PageIndex);
        Assert.AreEqual(12d, destination.X);
        Assert.AreEqual(34d, destination.Y);
        Assert.AreEqual(ReaderMath.MaximumZoom, destination.Zoom.GetValueOrDefault());
    }

    [TestMethod]
    public void OutlineNode_NormalizesBlankTitleAndChildren()
    {
        var node = new PdfOutlineNode("   ");

        Assert.AreEqual("Mục không tên", node.Title);
        Assert.AreEqual(0, node.Children.Count);
    }
}
