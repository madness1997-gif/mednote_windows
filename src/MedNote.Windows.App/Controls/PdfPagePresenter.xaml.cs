using System.ComponentModel;
using MedNote.Core;
using MedNote.Windows.App.Infrastructure;
using MedNote.Windows.App.ViewModels;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace MedNote.Windows.App.Controls;

public sealed partial class PdfPagePresenter : UserControl
{
    private PdfPageViewModel? _boundPage;
    private bool _renderLoopRunning;
    private bool _renderRequested;
    private int _surfaceRefreshQueued;
    private CanvasImageSource? _direct2DSurface;
    private CancellationTokenSource? _selectionCancellation;
    private PdfPagePoint _pendingSelectionPoint;
    private int? _selectionAnchorIndex;
    private long _selectionGeneration;
    private bool _selectionUpdateRequested;
    private bool _selectionUpdateRunning;
    private bool _isSelecting;
    private bool _selectionMarkupCommitted;

    public PdfPagePresenter()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Direct2DPageSurfaceFactory.SurfacesInvalidated += OnDirect2DSurfacesInvalidated;
        Microsoft.UI.Xaml.Media.CompositionTarget.SurfaceContentsLost += OnSurfaceContentsLost;
        BindPage(DataContext as PdfPageViewModel);
        PresentSurface(_boundPage?.Surface);
        DrawAnnotations();
        DrawSelection();
        DrawSourceFocus();
        RequestRender();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Direct2DPageSurfaceFactory.SurfacesInvalidated -= OnDirect2DSurfacesInvalidated;
        Microsoft.UI.Xaml.Media.CompositionTarget.SurfaceContentsLost -= OnSurfaceContentsLost;
        _renderRequested = false;
        CancelSelectionInteraction();
        CancelAnnotationGesture();
        ClearDirect2DSurface();
        AnnotationCanvas.Children.Clear();
        SelectionCanvas.Children.Clear();
        SourceFocusCanvas.Children.Clear();
        InteractionCanvas.Children.Clear();
        BindPage(null);
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        BindPage(args.NewValue as PdfPageViewModel);
        if (IsLoaded)
        {
            PresentSurface(_boundPage?.Surface);
            DrawAnnotations();
            DrawSelection();
            DrawSourceFocus();
            RequestRender();
        }
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PdfPageViewModel.Surface))
        {
            var surface = (sender as PdfPageViewModel)?.Surface;
            PresentSurface(surface);
            if (IsLoaded && surface is null)
            {
                RequestRender();
            }
        }
        else if (IsLoaded && e.PropertyName is nameof(PdfPageViewModel.DisplayWidth)
            or nameof(PdfPageViewModel.DisplayHeight)
            or nameof(PdfPageViewModel.Rotation))
        {
            DrawAnnotations();
            DrawSelection();
            DrawSourceFocus();
            RequestRender();
        }
        else if (e.PropertyName == nameof(PdfPageViewModel.Selection))
        {
            DrawSelection();
        }
        else if (e.PropertyName == nameof(PdfPageViewModel.Annotations))
        {
            DrawAnnotations();
        }
        else if (e.PropertyName == nameof(PdfPageViewModel.SourceFocusRect))
        {
            DrawSourceFocus();
        }
    }

    private void BindPage(PdfPageViewModel? page)
    {
        if (ReferenceEquals(_boundPage, page))
        {
            return;
        }

        if (_boundPage is not null)
        {
            _boundPage.PropertyChanged -= OnPagePropertyChanged;
            _boundPage.Unpin();
        }

        CancelSelectionInteraction();
        CancelAnnotationGesture();
        _boundPage = page;
        if (_boundPage is not null)
        {
            _boundPage.PropertyChanged += OnPagePropertyChanged;
        }
        else
        {
            ClearDirect2DSurface();
        }
    }

    private async void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var page = _boundPage;
        var point = e.GetCurrentPoint(PageInteractionLayer);
        if (page is null || !point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (!page.IsTextSelectionEnabled)
        {
            BeginAnnotationGesture(page, e, point);
            return;
        }

        CancelSelectionInteraction();
        _selectionCancellation = new CancellationTokenSource();
        var generation = ++_selectionGeneration;
        _isSelecting = true;
        _selectionMarkupCommitted = false;
        page.ClearTextSelection();
        PageInteractionLayer.CapturePointer(e.Pointer);
        e.Handled = true;

        try
        {
            var index = await page.GetTextIndexAtDisplayPointAsync(
                new PdfPagePoint(point.Position.X, point.Position.Y),
                cancellationToken: _selectionCancellation.Token);
            if (generation != _selectionGeneration || !ReferenceEquals(page, _boundPage) || index is null)
            {
                return;
            }

            _selectionAnchorIndex = index;
            await page.SelectTextBetweenAsync(index.Value, index.Value, _selectionCancellation.Token);
            if (!_isSelecting)
            {
                CommitAutomaticSelectionMarkup(page);
            }
        }
        catch (OperationCanceledException)
        {
            // A recycled page or newer pointer gesture superseded this one.
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_gestureTool is not null)
        {
            UpdateAnnotationGesture(e);
            return;
        }

        if (!_isSelecting || _selectionAnchorIndex is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(PageInteractionLayer);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        QueueSelectionUpdate(new PdfPagePoint(point.Position.X, point.Position.Y));
        e.Handled = true;
    }

    private async void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_gestureTool is not null)
        {
            await FinishAnnotationGestureAsync(e);
            return;
        }

        if (!_isSelecting)
        {
            return;
        }

        var point = e.GetCurrentPoint(PageInteractionLayer);
        QueueSelectionUpdate(new PdfPagePoint(point.Position.X, point.Position.Y));
        _isSelecting = false;
        PageInteractionLayer.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isSelecting = false;
        CancelAnnotationGesture();
    }

    private void QueueSelectionUpdate(PdfPagePoint point)
    {
        _pendingSelectionPoint = point;
        _selectionUpdateRequested = true;
        if (!_selectionUpdateRunning)
        {
            _ = RunSelectionUpdateLoopAsync();
        }
    }

    private async Task RunSelectionUpdateLoopAsync()
    {
        _selectionUpdateRunning = true;
        var generation = _selectionGeneration;
        try
        {
            while (_selectionUpdateRequested && generation == _selectionGeneration)
            {
                _selectionUpdateRequested = false;
                var page = _boundPage;
                var anchorIndex = _selectionAnchorIndex;
                var cancellationToken = _selectionCancellation?.Token ?? CancellationToken.None;
                if (page is null || anchorIndex is null)
                {
                    return;
                }

                var focusIndex = await page.GetTextIndexAtDisplayPointAsync(
                    _pendingSelectionPoint,
                    cancellationToken: cancellationToken);
                if (focusIndex is not null
                    && generation == _selectionGeneration
                    && ReferenceEquals(page, _boundPage))
                {
                    await page.SelectTextBetweenAsync(anchorIndex.Value, focusIndex.Value, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A newer selection gesture owns the overlay now.
        }
        finally
        {
            _selectionUpdateRunning = false;
            if (_selectionUpdateRequested && IsLoaded)
            {
                _ = RunSelectionUpdateLoopAsync();
            }
            else if (!_isSelecting && _boundPage is { } page)
            {
                CommitAutomaticSelectionMarkup(page);
            }
        }
    }

    private void CommitAutomaticSelectionMarkup(PdfPageViewModel page)
    {
        if (!_selectionMarkupCommitted && page.CommitSelectionMarkupForActiveTool())
        {
            _selectionMarkupCommitted = true;
        }
    }

    private void DrawSelection()
    {
        SelectionCanvas.Children.Clear();
        var page = _boundPage;
        if (page is null)
        {
            return;
        }

        foreach (var bounds in page.GetDisplaySelectionBounds())
        {
            var highlight = new Rectangle
            {
                Width = bounds.Width,
                Height = bounds.Height,
                Fill = new SolidColorBrush(ColorHelper.FromArgb(82, 48, 155, 190)),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(highlight, bounds.Left);
            Canvas.SetTop(highlight, bounds.Top);
            SelectionCanvas.Children.Add(highlight);
        }
    }

    private void OnCopySelectionClicked(object sender, RoutedEventArgs e)
    {
        var text = _boundPage?.Selection?.Text;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private async void OnDictionarySelectionClicked(object sender, RoutedEventArgs e)
    {
        var text = _boundPage?.Selection?.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        const int maximumLookupLength = 512;
        text = text[..Math.Min(text.Length, maximumLookupLength)];
        var uri = new Uri(
            $"https://translate.google.com/?sl=en&tl=vi&text={Uri.EscapeDataString(text)}&op=translate");
        await Launcher.LaunchUriAsync(uri);
    }

    private void CancelSelectionInteraction()
    {
        _selectionCancellation?.Cancel();
        _selectionCancellation?.Dispose();
        _selectionCancellation = null;
        _selectionAnchorIndex = null;
        _selectionUpdateRequested = false;
        _isSelecting = false;
        _selectionGeneration++;
    }

    private void RequestRender()
    {
        _renderRequested = true;
        if (_renderLoopRunning)
        {
            return;
        }

        _ = RunRenderLoopAsync();
    }

    private async Task RunRenderLoopAsync()
    {
        _renderLoopRunning = true;
        try
        {
            while (_renderRequested && IsLoaded)
            {
                _renderRequested = false;
                var page = _boundPage;
                if (page is null)
                {
                    return;
                }

                await page.PinAndRenderAsync();
                if (!ReferenceEquals(page, _boundPage))
                {
                    page.Unpin();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Recycling or a newer layout invalidated the current render.
        }
        catch (ObjectDisposedException)
        {
            // The document session is shutting down.
        }
        finally
        {
            _renderLoopRunning = false;
            if (_renderRequested && IsLoaded)
            {
                RequestRender();
            }
        }
    }

    private void PresentSurface(RenderedPdfPage? surface)
    {
        ClearDirect2DSurface();
        if (!IsLoaded || surface is null)
        {
            return;
        }

        try
        {
            _direct2DSurface = Direct2DPageSurfaceFactory.Create(surface);
            PageImage.Source = _direct2DSurface;
            if (_boundPage is not null)
            {
                _boundPage.ReportPresentationSucceeded();
                if (RenderProbe.SignalPagePresented(
                    _boundPage.Number,
                    _boundPage.OwnerPageCount,
                    _boundPage.Rotation,
                    surface))
                {
                    Direct2DPageSurfaceFactory.RequestSurfaceRecreation();
                }
            }
        }
        catch (Exception exception)
        {
            _boundPage?.ReportPresentationError(exception);
        }
    }

    private void ClearDirect2DSurface()
    {
        PageImage.Source = null;
        _direct2DSurface = null;
    }

    private void OnDirect2DSurfacesInvalidated(object? sender, EventArgs e) => QueueSurfaceRefresh();

    private void OnSurfaceContentsLost(object? sender, object e) => QueueSurfaceRefresh();

    private void QueueSurfaceRefresh()
    {
        if (Interlocked.Exchange(ref _surfaceRefreshQueued, 1) != 0)
        {
            return;
        }

        if (!DispatcherQueue.TryEnqueue(() =>
            {
                Interlocked.Exchange(ref _surfaceRefreshQueued, 0);
                if (IsLoaded)
                {
                    PresentSurface(_boundPage?.Surface);
                }
            }))
        {
            Interlocked.Exchange(ref _surfaceRefreshQueued, 0);
        }
    }
}
