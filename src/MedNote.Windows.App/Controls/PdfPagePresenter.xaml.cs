using System.ComponentModel;
using MedNote.Windows.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedNote.Windows.App.Controls;

public sealed partial class PdfPagePresenter : UserControl
{
    private PdfPageViewModel? _boundPage;
    private bool _renderLoopRunning;
    private bool _renderRequested;

    public PdfPagePresenter()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BindPage(DataContext as PdfPageViewModel);
        RequestRender();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _renderRequested = false;
        BindPage(null);
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        BindPage(args.NewValue as PdfPageViewModel);
        if (IsLoaded)
        {
            RequestRender();
        }
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsLoaded && e.PropertyName is nameof(PdfPageViewModel.DisplayWidth)
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
}
