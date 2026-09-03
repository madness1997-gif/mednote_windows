using MedNote.Core;
using MedNote.Windows.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace MedNote.Windows.App.Controls;

public sealed partial class PdfPagePresenter
{
    private PdfTool? _gestureTool;
    private PdfPagePoint _gestureStart;
    private PdfPagePoint _gestureCurrent;
    private readonly List<PdfAnnotationPoint> _inkPoints = [];
    private readonly HashSet<string> _erasedAnnotationIds = new(StringComparer.Ordinal);

    private void BeginAnnotationGesture(
        PdfPageViewModel page,
        PointerRoutedEventArgs args,
        Microsoft.UI.Input.PointerPoint point)
    {
        var tool = page.ActiveTool;
        if (tool is not PdfTool.Pen
            and not PdfTool.Eraser
            and not PdfTool.AreaHighlight
            and not PdfTool.Crop
            and not PdfTool.Rectangle
            and not PdfTool.Ellipse
            and not PdfTool.Arrow)
        {
            return;
        }

        CancelSelectionInteraction();
        CancelAnnotationGesture();
        _gestureTool = tool;
        _gestureStart = new PdfPagePoint(point.Position.X, point.Position.Y);
        _gestureCurrent = _gestureStart;
        _inkPoints.Clear();
        _erasedAnnotationIds.Clear();
        PageInteractionLayer.CapturePointer(args.Pointer);
        if (tool == PdfTool.Pen)
        {
            AddInkPoint(page, point);
        }
        else if (tool == PdfTool.Eraser)
        {
            HitTestEraser(page, _gestureCurrent);
        }

        DrawInteractionPreview();
        args.Handled = true;
    }

    private void UpdateAnnotationGesture(PointerRoutedEventArgs args)
    {
        var page = _boundPage;
        if (page is null || _gestureTool is null)
        {
            return;
        }

        var point = args.GetCurrentPoint(PageInteractionLayer);
        _gestureCurrent = new PdfPagePoint(point.Position.X, point.Position.Y);
        if (_gestureTool == PdfTool.Pen)
        {
            foreach (var intermediate in args.GetIntermediatePoints(PageInteractionLayer).Reverse())
            {
                AddInkPoint(page, intermediate);
            }
        }
        else if (_gestureTool == PdfTool.Eraser)
        {
            HitTestEraser(page, _gestureCurrent);
        }

        DrawInteractionPreview();
        args.Handled = true;
    }

    private async Task FinishAnnotationGestureAsync(PointerRoutedEventArgs args)
    {
        var page = _boundPage;
        var tool = _gestureTool;
        if (page is null || tool is null)
        {
            CancelAnnotationGesture();
            return;
        }

        UpdateAnnotationGesture(args);
        var start = _gestureStart;
        var end = _gestureCurrent;
        var inkPoints = _inkPoints.ToArray();
        var erasedIds = _erasedAnnotationIds.ToArray();
        _gestureTool = null;
        _inkPoints.Clear();
        _erasedAnnotationIds.Clear();
        InteractionCanvas.Children.Clear();
        PageInteractionLayer.ReleasePointerCapture(args.Pointer);
        args.Handled = true;

        if (tool == PdfTool.Pen && inkPoints.Length > 0)
        {
            page.AddAnnotation(new PdfAnnotation
            {
                Id = ReaderViewModel.CreateAnnotationId(PdfAnnotationKind.Ink),
                Kind = PdfAnnotationKind.Ink,
                Page = page.Number,
                Color = page.InkColor,
                Width = page.DisplayStrokeWidthToPage(page.InkWidth),
                Points = inkPoints.ToList(),
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
            return;
        }

        if (tool == PdfTool.Eraser)
        {
            page.DeleteAnnotations(erasedIds);
            return;
        }

        var pixelWidth = Math.Abs(end.X - start.X);
        var pixelHeight = Math.Abs(end.Y - start.Y);
        var minimum = tool == PdfTool.Crop ? 10d : 4d;
        if (pixelWidth < minimum || pixelHeight < minimum)
        {
            return;
        }

        var rectangle = page.DisplayRectToAnnotation(start, end);
        if (tool == PdfTool.Crop)
        {
            try
            {
                await page.CreateCropAsync(rectangle);
            }
            catch (Exception exception)
            {
                page.ReportInteractionError($"Không crop được trang {page.Number}: {exception.Message}");
            }

            return;
        }

        if (tool == PdfTool.AreaHighlight)
        {
            page.AddAnnotation(new PdfAnnotation
            {
                Id = ReaderViewModel.CreateAnnotationId(PdfAnnotationKind.AreaHighlight),
                Kind = PdfAnnotationKind.AreaHighlight,
                Page = page.Number,
                Color = page.HighlightColor,
                Rects = [rectangle],
                Text = string.Empty,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
            return;
        }

        var kind = tool switch
        {
            PdfTool.Rectangle => PdfAnnotationKind.Rectangle,
            PdfTool.Ellipse => PdfAnnotationKind.Ellipse,
            PdfTool.Arrow => PdfAnnotationKind.Arrow,
            _ => throw new InvalidOperationException($"Công cụ annotation không hợp lệ: {tool}"),
        };
        page.AddAnnotation(new PdfAnnotation
        {
            Id = ReaderViewModel.CreateAnnotationId(kind),
            Kind = kind,
            Page = page.Number,
            Color = page.InkColor,
            Width = page.DisplayStrokeWidthToPage(page.InkWidth),
            Rect = rectangle,
            Text = string.Empty,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });
    }

    private void AddInkPoint(PdfPageViewModel page, Microsoft.UI.Input.PointerPoint point)
    {
        var pressure = point.Properties.Pressure > 0f ? point.Properties.Pressure : 0.5f;
        var next = page.DisplayPointToAnnotation(
            new PdfPagePoint(point.Position.X, point.Position.Y),
            pressure);
        if (_inkPoints.Count > 0)
        {
            var previous = _inkPoints[^1];
            if (Math.Abs(previous.X - next.X) < 0.05d && Math.Abs(previous.Y - next.Y) < 0.05d)
            {
                return;
            }
        }

        _inkPoints.Add(next);
    }

    private void HitTestEraser(PdfPageViewModel page, PdfPagePoint point)
    {
        const double radius = 14d;
        foreach (var annotation in page.Annotations)
        {
            if (_erasedAnnotationIds.Contains(annotation.Id))
            {
                continue;
            }

            var hit = annotation.Kind == PdfAnnotationKind.Ink
                ? HitInk(page, annotation.Points ?? [], point, radius)
                : AnnotationRects(annotation).Any(rectangle =>
                {
                    var display = page.AnnotationRectToDisplay(rectangle);
                    return point.X >= display.Left - radius
                        && point.X <= display.Right + radius
                        && point.Y >= display.Top - radius
                        && point.Y <= display.Bottom + radius;
                });
            if (hit)
            {
                _erasedAnnotationIds.Add(annotation.Id);
            }
        }
    }

    private static bool HitInk(
        PdfPageViewModel page,
        IReadOnlyList<PdfAnnotationPoint> points,
        PdfPagePoint target,
        double radius)
    {
        if (points.Count == 0)
        {
            return false;
        }

        var display = points.Select(page.AnnotationPointToDisplay).ToArray();
        if (display.Length == 1)
        {
            return Distance(display[0], target) <= radius;
        }

        return display.Skip(1).Select((point, index) =>
            PointSegmentDistance(target, display[index], point)).Any(distance => distance <= radius);
    }

    private static double PointSegmentDistance(PdfPagePoint point, PdfPagePoint start, PdfPagePoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        var ratio = lengthSquared > 0d
            ? Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0d, 1d)
            : 0d;
        return Distance(point, new PdfPagePoint(start.X + ratio * dx, start.Y + ratio * dy));
    }

    private static double Distance(PdfPagePoint left, PdfPagePoint right) =>
        Math.Sqrt(Math.Pow(left.X - right.X, 2d) + Math.Pow(left.Y - right.Y, 2d));

    private void OnHighlightSelectionClicked(object sender, RoutedEventArgs e) =>
        _boundPage?.AddSelectionMarkup(PdfAnnotationKind.Highlight);

    private void OnUnderlineSelectionClicked(object sender, RoutedEventArgs e) =>
        _boundPage?.AddSelectionMarkup(PdfAnnotationKind.Underline);

    private void OnStrikeoutSelectionClicked(object sender, RoutedEventArgs e) =>
        _boundPage?.AddSelectionMarkup(PdfAnnotationKind.Strikeout);

    private void OnSquigglySelectionClicked(object sender, RoutedEventArgs e) =>
        _boundPage?.AddSelectionMarkup(PdfAnnotationKind.Squiggly);

    private void CancelAnnotationGesture()
    {
        _gestureTool = null;
        _inkPoints.Clear();
        _erasedAnnotationIds.Clear();
        InteractionCanvas.Children.Clear();
    }
}
