using MedNote.Core;
using Xunit;

namespace MedNote.Core.Tests;

public sealed class PdfPageMetricsTests
{
    [Theory]
    [InlineData(612d, 792d, 0.7727272727d)]
    [InlineData(792d, 612d, 1.2941176471d)]
    public void AspectRatio_PreservesPlausiblePageMetrics(double width, double height, double expected)
    {
        var metrics = new PdfPageMetrics(width, height);

        Assert.Equal(expected, metrics.AspectRatio, precision: 8);
    }

    [Theory]
    [InlineData(0d, 792d)]
    [InlineData(60d, 792d)]
    [InlineData(double.NaN, 792d)]
    [InlineData(792d, 0d)]
    public void AspectRatio_FallsBackWhenPdfBoxesAreBroken(double width, double height)
    {
        var metrics = new PdfPageMetrics(width, height);

        Assert.Equal(1d / Math.Sqrt(2d), metrics.AspectRatio, precision: 12);
    }
}
