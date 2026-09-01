using System.ComponentModel;
using MedNote.Windows.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedNote.Windows.App.Controls;

public sealed partial class PdfPagePresenter : UserControl
{
    private CancellationTokenSource? _loadCancellation;
    private PdfPageViewModel? _boundPage;
    private bool _rendering;
    private bool _renderRequested;

    public PdfPagePresenter()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_boundPage is not null)
        {
            _boundPage.PropertyChanged -= OnPagePropertyChanged;
            _boundPage.PropertyChanged += OnPagePropertyChanged;
        }

        await RenderAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        _renderRequested = false;
        if (_boundPage is not null)
        {
            _boundPage.PropertyChanged -= OnPagePropertyChanged;
            _boundPage.Unpin();
        }
    }

    private async void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        var nextPage = DataContext as PdfPageViewModel;
        if (!ReferenceEquals(_boundPage, nextPage))
        {
            if (_boundPage is not null)
            {
                _boundPage.PropertyChanged -= OnPagePropertyChanged;
                _boundPage.Unpin();
            }

            _boundPage = nextPage;
            if (_boundPage is not null)
            {
                _boundPage.PropertyChanged += OnPagePropertyChanged;
            }
        }

        if (IsLoaded)
        {
            await RenderAsync();
        }
    }

    private async void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsLoaded && e.PropertyName is nameof(PdfPageViewModel.DisplayWidth)
            or nameof(PdfPageViewModel.DisplayHeight))
        {
            await RenderAsync();
        }
    }

    private async Task RenderAsync()
    {
        if (_rendering)
        {
            _renderRequested = true;
            return;
        }

        var page = DataContext as PdfPageViewModel;
        if (page is null)
        {
            return;
        }

        if (!ReferenceEquals(_boundPage, page))
        {
            if (_boundPage is not null)
            {
                _boundPage.PropertyChanged -= OnPagePropertyChanged;
                _boundPage.Unpin();
            }

            _boundPage = page;
            _boundPage.PropertyChanged += OnPagePropertyChanged;
        }

        _rendering = true;
        try
        {
            do
            {
                _renderRequested = false;
                _loadCancellation?.Cancel();
                _loadCancellation?.Dispose();
                _loadCancellation = new CancellationTokenSource();
                await page.PinAndRenderAsync(_loadCancellation.Token);
            }
            while (_renderRequested && IsLoaded && ReferenceEquals(page, DataContext));
        }
        catch (OperationCanceledException)
        {
            // The page was recycled before the render started.
        }
        catch (ObjectDisposedException)
        {
            // Window shutdown can dispose the session before the final unload.
        }
        finally
        {
            _rendering = false;
        }

        if (_renderRequested && IsLoaded)
        {
            _renderRequested = false;
            await RenderAsync();
        }
    }
}
