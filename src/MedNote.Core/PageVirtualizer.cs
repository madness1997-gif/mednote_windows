namespace MedNote.Core;

public sealed record PageVirtualMetrics(double[] Heights, double[] Offsets, double TotalHeight);

public readonly record struct PageVirtualRange(int Start, int End);

public static class PageVirtualizer
{
    public const double ContinuousPageGap = 22d;
    public const double ContinuousEstimatedHeight = 780d;
    public const double ContinuousOverscan = 1_400d;

    public static PageVirtualMetrics CalculateMetrics(
        IReadOnlyList<int> pages,
        IReadOnlyDictionary<int, double> measuredHeights,
        double estimatedHeight = ContinuousEstimatedHeight,
        double gap = ContinuousPageGap)
    {
        var fallback = Math.Max(260d, estimatedHeight);
        var heights = new double[pages.Count];
        var offsets = new double[pages.Count];
        var cursor = 0d;

        for (var index = 0; index < pages.Count; index++)
        {
            heights[index] = Math.Max(260d, measuredHeights.GetValueOrDefault(pages[index], fallback));
            offsets[index] = cursor;
            cursor += heights[index] + (index + 1 < pages.Count ? gap : 0d);
        }

        return new PageVirtualMetrics(heights, offsets, cursor);
    }

    public static PageVirtualRange VisibleRange(
        PageVirtualMetrics metrics,
        double viewportTop,
        double viewportHeight,
        double overscan = ContinuousOverscan)
    {
        var count = metrics.Offsets.Length;
        if (count == 0)
        {
            return new PageVirtualRange(0, 0);
        }

        var minimum = Math.Max(0d, viewportTop - overscan);
        var maximum = Math.Max(minimum, viewportTop + Math.Max(1d, viewportHeight) + overscan);
        var start = Math.Min(count - 1, FirstIndex(count, index => metrics.Offsets[index] + metrics.Heights[index] >= minimum));
        var end = Math.Max(start + 1, FirstIndex(count, index => metrics.Offsets[index] > maximum));
        return new PageVirtualRange(start, Math.Min(count, end));
    }

    public static int AnchorIndex(PageVirtualMetrics metrics, double offset)
    {
        var count = metrics.Offsets.Length;
        if (count == 0)
        {
            return -1;
        }

        var next = FirstIndex(count, index => metrics.Offsets[index] >= offset);
        if (next <= 0)
        {
            return 0;
        }

        if (next >= count)
        {
            return count - 1;
        }

        var previous = next - 1;
        return offset <= metrics.Offsets[previous] + metrics.Heights[previous] ? previous : next;
    }

    public static double AnchorTargetOffset(double absoluteOffset, double? pageOffsetRatio, double viewportOffset, double pageHeight) =>
        pageOffsetRatio is null ? absoluteOffset : viewportOffset - pageOffsetRatio.Value * Math.Max(1d, pageHeight);

    private static int FirstIndex(int count, Func<int, bool> predicate)
    {
        var low = 0;
        var high = count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (predicate(middle))
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return low;
    }
}
