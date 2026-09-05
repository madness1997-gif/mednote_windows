using MedNote.Core;

namespace MedNote.Windows.App.ViewModels;

public sealed partial class ReaderViewModel
{
    public int GoToPage(int requestedPage)
    {
        if (!HasDocument)
        {
            return 1;
        }

        var nextPage = ReaderMath.ClampPage(requestedPage, PageCount);
        CurrentPage = nextPage;
        _reader = _reader with { Page = nextPage };
        _position = _position with { AnchorPage = nextPage };
        QueuePersist();
        return nextPage;
    }

    public void SetZoom(double zoom)
    {
        var normalized = ReaderMath.ClampZoom(zoom);
        if (Math.Abs(normalized - Zoom) < 0.001d)
        {
            return;
        }

        Zoom = normalized;
        _reader = _reader with { Zoom = normalized };
        RefreshAllPageLayouts();
        QueuePersist();
    }

    public void StepZoom(int direction) => SetZoom(ReaderMath.StepZoom(Zoom, direction));

    public void SetRotation(int rotation)
    {
        var normalized = ReaderMath.NormalizeRotation(rotation);
        if (Rotation == normalized)
        {
            return;
        }

        Rotation = normalized;
        _reader = _reader with { Rotation = normalized };
        RefreshAllPageLayouts(normalized);
        QueuePersist();
    }

    public void SetFitMode(PdfFitMode mode)
    {
        if (FitMode == mode)
        {
            return;
        }

        FitMode = mode;
        _reader = _reader with { FitMode = mode };
        RefreshAllPageLayouts();
        QueuePersist();
    }

    public void SetViewMode(PdfViewMode mode)
    {
        if (ViewMode == mode)
        {
            return;
        }

        ViewMode = mode;
        _reader = _reader with { ViewMode = mode };
        RefreshAllPageLayouts();
        QueuePersist();
    }

    public void SetActiveTool(PdfTool tool)
    {
        if (ActiveTool == tool)
        {
            return;
        }

        ActiveTool = tool;
        if (tool is not PdfTool.Smart
            and not PdfTool.Select
            and not PdfTool.Highlight
            and not PdfTool.Underline
            and not PdfTool.Strikeout
            and not PdfTool.Squiggly)
        {
            ClearTextSelection();
        }
    }

    public bool ToggleBookmark()
    {
        if (!HasDocument)
        {
            return false;
        }

        var bookmarks = _reader.Bookmarks.ToHashSet();
        var added = bookmarks.Add(CurrentPage);
        if (!added)
        {
            bookmarks.Remove(CurrentPage);
        }

        var ordered = bookmarks.Order().ToList();
        _reader = _reader with { Bookmarks = ordered };
        Bookmarks = ordered;
        QueuePersist();
        return added;
    }

    public void RemoveBookmark(int page)
    {
        if (!_reader.Bookmarks.Contains(page)) return;
        var remaining = _reader.Bookmarks.Where(value => value != page).ToList();
        _reader = _reader with { Bookmarks = remaining };
        Bookmarks = remaining;
        QueuePersist();
    }

    public void SetViewport(double width, double height, double rasterizationScale)
    {
        width = Math.Max(320d, width);
        height = Math.Max(320d, height);
        rasterizationScale = Math.Clamp(rasterizationScale, 1d, 3d);
        if (Math.Abs(_viewportWidth - width) < 1d
            && Math.Abs(_viewportHeight - height) < 1d
            && Math.Abs(RasterizationScale - rasterizationScale) < 0.01d)
        {
            return;
        }

        _viewportWidth = width;
        _viewportHeight = height;
        RasterizationScale = rasterizationScale;
        RefreshAllPageLayouts();
    }

    public void RefreshPageLayout(PdfPageViewModel page)
    {
        var layout = CalculatePageLayout(page.AspectRatio);
        page.SetLayout(layout.Width, layout.Height, notify: true);
    }

    public void CapturePosition(ReaderPosition position)
    {
        if (!HasDocument)
        {
            return;
        }

        _position = position.Normalize(PageCount);
        if (_position.AnchorPage != CurrentPage)
        {
            CurrentPage = _position.AnchorPage;
            _reader = _reader with { Page = CurrentPage };
        }

        QueuePersist();
    }

    private (double Width, double Height) CalculatePageLayout(double aspectRatio)
    {
        var horizontalMargin = ViewMode == PdfViewMode.Continuous ? 56d : 36d;
        var verticalMargin = ViewMode == PdfViewMode.Continuous ? 42d : 36d;
        var availableWidth = Math.Max(280d, _viewportWidth - horizontalMargin);
        var availableHeight = Math.Max(280d, _viewportHeight - verticalMargin);
        var baseWidth = FitMode == PdfFitMode.Width || ViewMode == PdfViewMode.Continuous
            ? availableWidth
            : Math.Min(availableWidth, availableHeight * aspectRatio);
        var width = baseWidth * Zoom;
        return (width, width / Math.Max(0.05d, aspectRatio));
    }

    private void RefreshAllPageLayouts(int? rotation = null)
    {
        foreach (var page in Pages)
        {
            var pageRotation = rotation ?? page.Rotation;
            var layout = CalculatePageLayout(page.AspectRatioForRotation(pageRotation));
            page.SetLayout(
                layout.Width,
                layout.Height,
                pageRotation,
                notify: page.IsPinned || ReferenceEquals(page, CurrentPageItem));
        }
    }
}
