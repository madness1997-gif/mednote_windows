using MedNote.Core;
using MedNote.Windows.App.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace MedNote.Windows.App.Controllers;

/// <summary>
/// Owns the Reader viewport lifecycle: virtualization scroll tracking, stable
/// page anchors, mode restoration, and mouse-drag panning. MainWindow only
/// routes commands and chooses which chrome is visible.
/// </summary>
public sealed class ReaderViewportController : IDisposable
{
    private readonly ReaderViewModel _viewModel;
    private readonly Grid _surface;
    private readonly ListView _continuousPages;
    private readonly ScrollViewer _singlePage;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherTimer _positionTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private ScrollViewer? _continuousScrollViewer;
    private ReaderPosition? _pendingPosition;
    private Point _panStart;
    private double _panHorizontalOffset;
    private double _panVerticalOffset;
    private bool _continuousScrollHooked;
    private bool _isPanning;
    private bool _restoringPosition;
    private bool _initialized;
    private bool _disposed;

    public ReaderViewportController(
        ReaderViewModel viewModel,
        Grid surface,
        ListView continuousPages,
        ScrollViewer singlePage,
        DispatcherQueue dispatcherQueue)
    {
        _viewModel = viewModel;
        _surface = surface;
        _continuousPages = continuousPages;
        _singlePage = singlePage;
        _dispatcherQueue = dispatcherQueue;
    }

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _positionTimer.Tick += OnPositionTimerTick;
        _singlePage.ViewChanged += OnSingleViewChanged;
        _surface.SizeChanged += OnSurfaceSizeChanged;
        _surface.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
        _surface.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointerMoved), true);
        _surface.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
        _surface.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnPointerReleased), true);
        EnsureContinuousScrollViewer();
    }

    public int NavigateToPage(int requestedPage, bool disableAnimation = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var page = _viewModel.GoToPage(requestedPage);
        if (_viewModel.ViewMode == PdfViewMode.Continuous && page <= _viewModel.Pages.Count)
        {
            _continuousPages.ScrollIntoView(_viewModel.Pages[page - 1], ScrollIntoViewAlignment.Leading);
        }
        else
        {
            _singlePage.ChangeView(0, 0, null, disableAnimation);
        }

        return page;
    }

    public void OnViewModeApplied()
    {
        if (_viewModel.ViewMode == PdfViewMode.Continuous)
        {
            EnsureContinuousScrollViewer();
        }
    }

    public void CaptureCurrentPosition()
    {
        if (!_viewModel.HasDocument)
        {
            return;
        }

        var position = _viewModel.ViewMode == PdfViewMode.Continuous
            ? ReadContinuousPosition()
            : ReadSinglePosition();
        if (position is not null)
        {
            _viewModel.CapturePosition(position);
            _pendingPosition = null;
        }
    }

    public async Task RestoreSavedPositionAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_viewModel.HasDocument)
        {
            return;
        }

        _restoringPosition = true;
        try
        {
            var position = _viewModel.SavedPosition.Normalize(_viewModel.PageCount);
            _viewModel.GoToPage(position.AnchorPage);
            if (_viewModel.ViewMode == PdfViewMode.Single)
            {
                await NextUiTurnAsync();
                var height = _viewModel.CurrentPageItem?.DisplayHeight ?? 0d;
                _singlePage.ChangeView(
                    position.HorizontalOffset,
                    position.PageOffsetRatio * height,
                    null,
                    true);
                return;
            }

            var item = _viewModel.Pages[position.AnchorPage - 1];
            _continuousPages.ScrollIntoView(item, ScrollIntoViewAlignment.Leading);
            await NextUiTurnAsync();
            await NextUiTurnAsync();
            EnsureContinuousScrollViewer();
            var container = _continuousPages.ContainerFromItem(item) as FrameworkElement;
            if (_continuousScrollViewer is not null && container is not null)
            {
                _continuousScrollViewer.ChangeView(
                    position.HorizontalOffset,
                    _continuousScrollViewer.VerticalOffset + (position.PageOffsetRatio * container.ActualHeight),
                    null,
                    true);
            }
        }
        finally
        {
            await NextUiTurnAsync();
            _restoringPosition = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _positionTimer.Stop();
        _positionTimer.Tick -= OnPositionTimerTick;
        _pendingPosition = null;
        _singlePage.ViewChanged -= OnSingleViewChanged;
        _surface.SizeChanged -= OnSurfaceSizeChanged;
        _surface.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed));
        _surface.RemoveHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointerMoved));
        _surface.RemoveHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPointerReleased));
        _surface.RemoveHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnPointerReleased));
        if (_isPanning)
        {
            _surface.ReleasePointerCaptures();
            _isPanning = false;
        }

        if (_continuousScrollViewer is not null && _continuousScrollHooked)
        {
            _continuousScrollViewer.ViewChanged -= OnContinuousViewChanged;
            _continuousScrollHooked = false;
        }
    }

    private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_surface.XamlRoot is not null)
        {
            _viewModel.SetViewport(e.NewSize.Width, e.NewSize.Height, _surface.XamlRoot.RasterizationScale);
        }
    }

    private void OnContinuousViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_restoringPosition)
        {
            return;
        }

        var position = ReadContinuousPosition();
        if (position is not null)
        {
            QueuePosition(position);
        }
    }

    private void OnSingleViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (!_restoringPosition && _viewModel.ViewMode == PdfViewMode.Single)
        {
            QueuePosition(ReadSinglePosition());
        }
    }

    private ReaderPosition? ReadContinuousPosition()
    {
        if (_continuousScrollViewer is null
            || _continuousPages.ItemsPanelRoot is not ItemsStackPanel panel
            || _viewModel.Pages.Count == 0)
        {
            return null;
        }

        var index = Math.Clamp(panel.FirstVisibleIndex, 0, _viewModel.Pages.Count - 1);
        var container = _continuousPages.ContainerFromIndex(index) as FrameworkElement;
        if (container is null || container.ActualHeight <= 1)
        {
            return null;
        }

        var location = container.TransformToVisual(_continuousPages).TransformPoint(new Point());
        return new ReaderPosition
        {
            AnchorPage = index + 1,
            PageOffsetRatio = Math.Clamp(-location.Y / container.ActualHeight, 0d, 1d),
            HorizontalOffset = _continuousScrollViewer.HorizontalOffset,
        };
    }

    private ReaderPosition ReadSinglePosition()
    {
        var height = _viewModel.CurrentPageItem?.DisplayHeight ?? 1d;
        return new ReaderPosition
        {
            AnchorPage = _viewModel.CurrentPage,
            PageOffsetRatio = Math.Clamp(_singlePage.VerticalOffset / Math.Max(1d, height), 0d, 1d),
            HorizontalOffset = _singlePage.HorizontalOffset,
        };
    }

    private void QueuePosition(ReaderPosition position)
    {
        _pendingPosition = position;
        _positionTimer.Stop();
        _positionTimer.Start();
    }

    private void OnPositionTimerTick(object? sender, object e)
    {
        _positionTimer.Stop();
        var position = _pendingPosition;
        _pendingPosition = null;
        if (position is null)
        {
            return;
        }

        _viewModel.CapturePosition(position);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_viewModel.ActiveTool != PdfTool.Pan || !e.GetCurrentPoint(_surface).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var scrollViewer = ActiveScrollViewer();
        if (scrollViewer is null)
        {
            return;
        }

        _isPanning = _surface.CapturePointer(e.Pointer);
        if (!_isPanning)
        {
            return;
        }

        _panStart = e.GetCurrentPoint(_surface).Position;
        _panHorizontalOffset = scrollViewer.HorizontalOffset;
        _panVerticalOffset = scrollViewer.VerticalOffset;
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        var scrollViewer = ActiveScrollViewer();
        if (scrollViewer is null)
        {
            return;
        }

        var current = e.GetCurrentPoint(_surface).Position;
        scrollViewer.ChangeView(
            _panHorizontalOffset - (current.X - _panStart.X),
            _panVerticalOffset - (current.Y - _panStart.Y),
            null,
            true);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        _surface.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private ScrollViewer? ActiveScrollViewer() =>
        _viewModel.ViewMode == PdfViewMode.Continuous ? EnsureContinuousScrollViewer() : _singlePage;

    private ScrollViewer? EnsureContinuousScrollViewer()
    {
        _continuousScrollViewer ??= FindDescendant<ScrollViewer>(_continuousPages);
        if (_continuousScrollViewer is not null && !_continuousScrollHooked)
        {
            _continuousScrollViewer.ViewChanged += OnContinuousViewChanged;
            _continuousScrollHooked = true;
        }

        return _continuousScrollViewer;
    }

    private Task NextUiTurnAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcherQueue.TryEnqueue(() => completion.TrySetResult()))
        {
            completion.TrySetResult();
        }

        return completion.Task;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
