using MedNote.Windows.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MedNote.Windows.App.Controls;

public sealed partial class PdfPagePresenter : UserControl
{
    private CancellationTokenSource? _loadCancellation;
    private PdfPageViewModel? _boundPage;

    public PdfPagePresenter()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await RenderAsync();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        _boundPage?.Unpin();
    }

    private async void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        var nextPage = DataContext as PdfPageViewModel;
        if (!ReferenceEquals(_boundPage, nextPage))
        {
            _boundPage?.Unpin();
            _boundPage = nextPage;
        }

        if (IsLoaded)
        {
            await RenderAsync();
        }
    }

    private async void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsLoaded && (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 1d
            || Math.Abs(e.NewSize.Height - e.PreviousSize.Height) > 1d))
        {
            await RenderAsync();
        }
    }

    private async Task RenderAsync()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var page = DataContext as PdfPageViewModel;
        if (page is null)
        {
            return;
        }

        if (!ReferenceEquals(_boundPage, page))
        {
            _boundPage?.Unpin();
            _boundPage = page;
        }

        try
        {
            await page.PinAndRenderAsync(_loadCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // The page was recycled before the render started.
        }
        catch (ObjectDisposedException)
        {
            // Window shutdown can dispose the session before the final unload.
        }
    }
}
