using MedNote.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace MedNote.Windows.App.Controls;

public sealed partial class PdfPagePresenter
{
    private void DrawAnnotations()
    {
        AnnotationCanvas.Children.Clear();
        var page = _boundPage;
        if (page is null)
        {
            return;
        }

        foreach (var annotation in page.Annotations)
        {
            var brush = new SolidColorBrush(ParseColor(annotation.Color));
            if (annotation.Kind == PdfAnnotationKind.Ink)
            {
                AddPolyline(
                    AnnotationCanvas,
                    (annotation.Points ?? []).Select(page.AnnotationPointToDisplay),
                    brush,
                    page.PageStrokeWidthToDisplay(annotation.Width ?? 1d));
                continue;
            }

            if (annotation.IsMarkup)
            {
                foreach (var source in annotation.Rects ?? [])
                {
                    DrawMarkup(page.AnnotationRectToDisplay(source), annotation.Kind, brush);
                }

                continue;
            }

            if (annotation.Rect is not { } objectRect)
            {
                continue;
            }

            DrawObject(
                page.AnnotationRectToDisplay(objectRect),
                annotation,
                brush,
                page.PageStrokeWidthToDisplay(annotation.Width ?? 1d));
        }
    }

    private void DrawMarkup(
        PdfPageRect bounds,
        PdfAnnotationKind kind,
        SolidColorBrush brush)
    {
        if (kind is PdfAnnotationKind.Highlight or PdfAnnotationKind.AreaHighlight)
        {
            AddPositioned(
                AnnotationCanvas,
                new Rectangle
                {
                    Width = bounds.Width,
                    Height = bounds.Height,
                    Fill = brush,
                    Opacity = 0.34d,
                },
                bounds.Left,
                bounds.Top);
            return;
        }

        if (kind == PdfAnnotationKind.Squiggly)
        {
            var step = Math.Max(2.4d, Math.Min(5d, bounds.Height * 0.24d));
            var points = new List<PdfPagePoint>();
            for (var x = bounds.Left; x < bounds.Right; x += step)
            {
                points.Add(new PdfPagePoint(x, bounds.Bottom - 1.2d));
                points.Add(new PdfPagePoint(Math.Min(bounds.Right, x + step / 2d), bounds.Bottom - 2.8d));
                points.Add(new PdfPagePoint(Math.Min(bounds.Right, x + step), bounds.Bottom - 1.2d));
            }

            AddPolyline(AnnotationCanvas, points, brush, 1.2d);
            return;
        }

        var y = kind == PdfAnnotationKind.Strikeout
            ? bounds.Top + bounds.Height * 0.52d
            : bounds.Bottom - 1d;
        AnnotationCanvas.Children.Add(new Line
        {
            X1 = bounds.Left,
            X2 = bounds.Right,
            Y1 = y,
            Y2 = y,
            Stroke = brush,
            StrokeThickness = 1.4d,
        });
    }

    private void DrawObject(
        PdfPageRect bounds,
        PdfAnnotation annotation,
        SolidColorBrush brush,
        double strokeWidth)
    {
        strokeWidth = Math.Max(1d, strokeWidth);
        switch (annotation.Kind)
        {
            case PdfAnnotationKind.Rectangle:
                AddPositioned(
                    AnnotationCanvas,
                    new Rectangle
                    {
                        Width = bounds.Width,
                        Height = bounds.Height,
                        Stroke = brush,
                        StrokeThickness = strokeWidth,
                    },
                    bounds.Left,
                    bounds.Top);
                break;
            case PdfAnnotationKind.Ellipse:
                AddPositioned(
                    AnnotationCanvas,
                    new Ellipse
                    {
                        Width = bounds.Width,
                        Height = bounds.Height,
                        Stroke = brush,
                        StrokeThickness = strokeWidth,
                    },
                    bounds.Left,
                    bounds.Top);
                break;
            case PdfAnnotationKind.Arrow:
                AddArrow(AnnotationCanvas, bounds, brush, strokeWidth);
                break;
            case PdfAnnotationKind.Note:
                AddPositioned(
                    AnnotationCanvas,
                    new Border
                    {
                        Width = bounds.Width,
                        Height = bounds.Height,
                        Background = brush,
                        Opacity = 0.84d,
                        CornerRadius = new CornerRadius(3d),
                        Child = new TextBlock
                        {
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                            Foreground = new SolidColorBrush(Colors.White),
                            Text = "!",
                        },
                    },
                    bounds.Left,
                    bounds.Top);
                break;
            case PdfAnnotationKind.Text:
            case PdfAnnotationKind.Stamp:
            case PdfAnnotationKind.Signature:
                var text = new TextBlock
                {
                    Width = bounds.Width,
                    Height = bounds.Height,
                    FontSize = Math.Clamp(bounds.Height * 0.32d, 8d, 22d),
                    FontStyle = annotation.Kind == PdfAnnotationKind.Signature
                        ? Windows.UI.Text.FontStyle.Italic
                        : Windows.UI.Text.FontStyle.Normal,
                    FontWeight = annotation.Kind == PdfAnnotationKind.Stamp
                        ? Microsoft.UI.Text.FontWeights.Bold
                        : Microsoft.UI.Text.FontWeights.Normal,
                    Foreground = brush,
                    Text = annotation.Text ?? string.Empty,
                    TextWrapping = TextWrapping.Wrap,
                };
                if (annotation.Kind == PdfAnnotationKind.Stamp)
                {
                    AddPositioned(
                        AnnotationCanvas,
                        new Border
                        {
                            Width = bounds.Width,
                            Height = bounds.Height,
                            BorderBrush = brush,
                            BorderThickness = new Thickness(strokeWidth),
                            Padding = new Thickness(4d),
                            Child = text,
                        },
                        bounds.Left,
                        bounds.Top);
                }
                else
                {
                    AddPositioned(AnnotationCanvas, text, bounds.Left, bounds.Top);
                }

                break;
        }
    }

    private void DrawInteractionPreview()
    {
        InteractionCanvas.Children.Clear();
        var page = _boundPage;
        var tool = _gestureTool;
        if (page is null || tool is null)
        {
            return;
        }

        if (tool == PdfTool.Pen)
        {
            AddPolyline(
                InteractionCanvas,
                _inkPoints.Select(page.AnnotationPointToDisplay),
                new SolidColorBrush(ParseColor(page.InkColor)),
                page.InkWidth);
            return;
        }

        if (tool == PdfTool.Eraser)
        {
            AddPositioned(
                InteractionCanvas,
                new Ellipse
                {
                    Width = 28d,
                    Height = 28d,
                    Stroke = new SolidColorBrush(ColorHelper.FromArgb(190, 180, 35, 24)),
                    StrokeThickness = 1.2d,
                    Fill = new SolidColorBrush(ColorHelper.FromArgb(28, 180, 35, 24)),
                },
                _gestureCurrent.X - 14d,
                _gestureCurrent.Y - 14d);
            return;
        }

        var bounds = DisplayBounds(_gestureStart, _gestureCurrent);
        var brush = new SolidColorBrush(ParseColor(
            tool == PdfTool.AreaHighlight ? page.HighlightColor : page.InkColor));
        if (tool == PdfTool.AreaHighlight)
        {
            AddPositioned(
                InteractionCanvas,
                new Rectangle
                {
                    Width = bounds.Width,
                    Height = bounds.Height,
                    Fill = brush,
                    Opacity = 0.34d,
                    Stroke = brush,
                    StrokeThickness = 1d,
                },
                bounds.Left,
                bounds.Top);
        }
        else if (tool == PdfTool.Rectangle || tool == PdfTool.Crop)
        {
            var preview = new Rectangle
            {
                Width = bounds.Width,
                Height = bounds.Height,
                Stroke = tool == PdfTool.Crop
                    ? new SolidColorBrush(ColorHelper.FromArgb(255, 14, 107, 112))
                    : brush,
                StrokeThickness = Math.Max(1d, page.InkWidth),
            };
            if (tool == PdfTool.Crop)
            {
                preview.StrokeDashArray = new DoubleCollection { 5d, 3d };
            }

            AddPositioned(InteractionCanvas, preview, bounds.Left, bounds.Top);
        }
        else if (tool == PdfTool.Ellipse)
        {
            AddPositioned(
                InteractionCanvas,
                new Ellipse
                {
                    Width = bounds.Width,
                    Height = bounds.Height,
                    Stroke = brush,
                    StrokeThickness = Math.Max(1d, page.InkWidth),
                },
                bounds.Left,
                bounds.Top);
        }
        else if (tool == PdfTool.Arrow)
        {
            AddArrow(InteractionCanvas, bounds, brush, Math.Max(1d, page.InkWidth));
        }
    }

    private static void AddPolyline(
        Canvas canvas,
        IEnumerable<PdfPagePoint> source,
        SolidColorBrush brush,
        double width)
    {
        var points = source.Select(point => new Point(point.X, point.Y)).ToArray();
        if (points.Length == 0)
        {
            return;
        }

        if (points.Length == 1)
        {
            var diameter = Math.Max(1d, width);
            AddPositioned(
                canvas,
                new Ellipse { Width = diameter, Height = diameter, Fill = brush },
                points[0].X - diameter / 2d,
                points[0].Y - diameter / 2d);
            return;
        }

        var polyline = new Polyline
        {
            Stroke = brush,
            StrokeThickness = Math.Max(1d, width),
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        foreach (var point in points)
        {
            polyline.Points.Add(point);
        }

        canvas.Children.Add(polyline);
    }

    private static void AddArrow(
        Canvas canvas,
        PdfPageRect bounds,
        SolidColorBrush brush,
        double width)
    {
        var start = new PdfPagePoint(bounds.Left, bounds.Top);
        var end = new PdfPagePoint(bounds.Right, bounds.Bottom);
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        var head = Math.Min(16d, Math.Max(7d, Math.Min(bounds.Width, bounds.Height) * 0.28d));
        canvas.Children.Add(NewLine(start, end, brush, width));
        canvas.Children.Add(NewLine(
            end,
            new PdfPagePoint(
                end.X + Math.Cos(angle + Math.PI * 0.78d) * head,
                end.Y + Math.Sin(angle + Math.PI * 0.78d) * head),
            brush,
            width));
        canvas.Children.Add(NewLine(
            end,
            new PdfPagePoint(
                end.X + Math.Cos(angle - Math.PI * 0.78d) * head,
                end.Y + Math.Sin(angle - Math.PI * 0.78d) * head),
            brush,
            width));
    }

    private static Line NewLine(
        PdfPagePoint start,
        PdfPagePoint end,
        SolidColorBrush brush,
        double width) => new()
    {
        X1 = start.X,
        Y1 = start.Y,
        X2 = end.X,
        Y2 = end.Y,
        Stroke = brush,
        StrokeThickness = width,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

    private static void AddPositioned(Canvas canvas, UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        canvas.Children.Add(element);
    }

    private static PdfPageRect DisplayBounds(PdfPagePoint first, PdfPagePoint second) => new(
        Math.Min(first.X, second.X),
        Math.Min(first.Y, second.Y),
        Math.Max(first.X, second.X),
        Math.Max(first.Y, second.Y));

    private static IReadOnlyList<PdfAnnotationRect> AnnotationRects(PdfAnnotation annotation)
    {
        if (annotation.Rects is { } rectangles)
        {
            return rectangles;
        }

        return annotation.Rect is { } rectangle ? [rectangle] : [];
    }

    private static Windows.UI.Color ParseColor(string color)
    {
        var value = PdfAnnotationColor.Normalize(color);
        return ColorHelper.FromArgb(
            255,
            Convert.ToByte(value.Substring(1, 2), 16),
            Convert.ToByte(value.Substring(3, 2), 16),
            Convert.ToByte(value.Substring(5, 2), 16));
    }
}
