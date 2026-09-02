using System.ComponentModel;
using MedNote.Core;
using MedNote.Windows.App.Infrastructure;
using MedNote.Windows.App.ViewModels;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedNote.Windows.App.Controls;

public sealed partial class PdfPagePresenter : UserControl
{
    private PdfPageViewModel? _boundPage;
    private bool _renderLoopRunning;
    private bool _renderRequested;
    private int _surfaceRefreshQueued;
    private CanvasImageSource? _direct2DSurface;

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
        RequestRender();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Direct2DPageSurfaceFactory.SurfacesInvalidated -= OnDirect2DSurfacesInvalidated;
        Microsoft.UI.Xaml.Media.CompositionTarget.SurfaceContentsLost -= OnSurfaceContentsLost;
        _renderRequested = false;
        ClearDirect2DSurface();
        BindPage(null);
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        BindPage(args.NewValue as PdfPageViewModel);
        if (IsLoaded)
        {
            PresentSurface(_boundPage?.Surface);
            RequestRender();
        }
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PdfPageViewModel.Surface))
        {
            PresentSurface((sender as PdfPageViewModel)?.Surface);
        }
        else if (IsLoaded && e.PropertyName is nameof(PdfPageViewModel.DisplayWidth)
            or nameof(PdfPageViewModel.DisplayHeight))
        {
            RequestRender();
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
                if (RenderProbe.SignalPagePresented(_boundPage.Number, _boundPage.OwnerPageCount, surface))
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
