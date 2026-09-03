using System.Buffers.Binary;
using MedNote.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class PngImageEncoderTests
{
    [TestMethod]
    public void EncodeBgra_WritesPngSignatureAndDimensions()
    {
        var bgra = new byte[]
        {
            0, 0, 255, 255,
            0, 255, 0, 255,
            255, 0, 0, 255,
            255, 255, 255, 255,
        };

        var png = PngImageEncoder.EncodeBgra(bgra, 2, 2, 8);

        CollectionAssert.AreEqual(
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
            png[..8]);
        Assert.AreEqual(2u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4)));
        Assert.AreEqual(2u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4)));
        CollectionAssert.AreEqual("IEND"u8.ToArray(), png[^8..^4]);
    }

    [TestMethod]
    public void EncodeBgra_RejectsShortRows()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            PngImageEncoder.EncodeBgra(new byte[8], 2, 2, 4));
    }
}
