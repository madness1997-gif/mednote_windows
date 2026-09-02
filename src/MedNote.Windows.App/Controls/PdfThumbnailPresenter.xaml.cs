using System.ComponentModel;
using MedNote.Windows.App.Infrastructure;
using MedNote.Windows.App.ViewModels;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedNote.Windows.App.Controls;

public sealed partial class PdfThumbnailPresenter : UserControl
{
    private PdfPageViewModel? _boundPage;
    private CanvasImageSource? _direct2DSurface;
    private int _surfaceRefreshQueued;

    public PdfThumbnailPresenter()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Direct2DPageSurfaceFactory.SurfacesInvalidated += OnSurfacesInvalidated;
        Microsoft.UI.Xaml.Media.CompositionTarget.SurfaceContentsLost += OnSurfaceContentsLost;
        BindPage(DataContext as PdfPageViewModel);
        PresentSurface(_boundPage?.ThumbnailSurface);
        _ = RenderAsync(_boundPage);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Direct2DPageSurfaceFactory.SurfacesInvalidated -= OnSurfacesInvalidated;
        Microsoft.UI.Xaml.Media.CompositionTarget.SurfaceContentsLost -= OnSurfaceContentsLost;
        BindPage(null);
        ClearSurface();
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        BindPage(args.NewValue as PdfPageViewModel);
        if (IsLoaded)
        {
            PresentSurface(_boundPage?.ThumbnailSurface);
            _ = RenderAsync(_boundPage);
        }
    }

    private async Task RenderAsync(PdfPageViewModel? page)
    {
        if (page is null)
        {
            return;
        }

        try
        {
            await page.PinAndRenderThumbnailAsync();
        }
        catch (OperationCanceledException)
        {
            // The ListView recycled this presenter.
        }
        catch (ObjectDisposedException)
        {
            // The document is closing.
        }
        catch
        {
            // The full page remains usable when an optional thumbnail fails.
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
            _boundPage.UnpinThumbnail();
        }

        _boundPage = page;
        if (_boundPage is not null)
        {
            _boundPage.PropertyChanged += OnPagePropertyChanged;
        }
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PdfPageViewModel.ThumbnailSurface))
        {
            PresentSurface((sender as PdfPageViewModel)?.ThumbnailSurface);
        }
    }

    private void PresentSurface(MedNote.Core.RenderedPdfPage? surface)
    {
        ClearSurface();
        if (!IsLoaded || surface is null)
        {
            return;
        }

        try
        {
            _direct2DSurface = Direct2DPageSurfaceFactory.Create(surface);
            ThumbnailImage.Source = _direct2DSurface;
        }
        catch
        {
            ClearSurface();
        }
    }

    private void ClearSurface()
    {
        ThumbnailImage.Source = null;
        _direct2DSurface = null;
    }

    private void OnSurfacesInvalidated(object? sender, EventArgs e) => QueueSurfaceRefresh();

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
                    PresentSurface(_boundPage?.ThumbnailSurface);
                }
            }))
        {
            Interlocked.Exchange(ref _surfaceRefreshQueued, 0);
        }
    }
}
