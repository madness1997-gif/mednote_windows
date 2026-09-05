using Microsoft.UI.Xaml;

namespace MedNote.Windows.App;

public sealed partial class MainWindow
{
    private void OnRemoveBookmarkClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Button { DataContext: int page }) ViewModel.RemoveBookmark(page);
    }
    private void OnOutlineTabChecked(object sender, RoutedEventArgs e)
    {
        if (!_initializingControls)
        {
            _sidebar?.SelectOutline();
        }
    }

    private void OnPagesTabChecked(object sender, RoutedEventArgs e)
    {
        if (!_initializingControls)
        {
            _sidebar?.SelectPages();
        }
    }

    private void OnSearchTabChecked(object sender, RoutedEventArgs e)
    {
        if (!_initializingControls)
        {
            _sidebar?.SelectSearch();
        }
    }

    private void OnBookmarksTabChecked(object sender, RoutedEventArgs e)
    {
        if (!_initializingControls)
        {
            _sidebar?.SelectBookmarks();
        }
    }

    private void OnHideRailClicked(object sender, RoutedEventArgs e) => _sidebar?.Hide();

    private void OnShowRailClicked(object sender, RoutedEventArgs e) => _sidebar?.Show();
}
