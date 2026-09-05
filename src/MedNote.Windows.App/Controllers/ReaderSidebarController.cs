using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MedNote.Windows.App.Controllers;

/// <summary>
/// Owns sidebar chrome state so MainWindow does not have to coordinate rail
/// visibility and mutually-exclusive tab buttons itself.
/// </summary>
public sealed class ReaderSidebarController
{
    private const double SidebarWidth = 264d;
    private const double ThumbnailWidth = 108d;
    private readonly Button _showRailButton;
    private double _selectedWidth = SidebarWidth;
    private readonly ColumnDefinition _sidebarColumn;
    private readonly FrameworkElement _sidebarPane;
    private readonly FrameworkElement _outlinePanel;
    private readonly FrameworkElement _pagesPanel;
    private readonly FrameworkElement _searchPanel;
    private readonly FrameworkElement _bookmarksPanel;
    private readonly ToggleButton _outlineButton;
    private readonly ToggleButton _pagesButton;
    private readonly ToggleButton _searchButton;
    private readonly ToggleButton _bookmarksButton;
    private bool _applying;
    private readonly FrameworkElement _annotationsPanel;
    private readonly ToggleButton _annotationsButton;

    public ReaderSidebarController(
        ColumnDefinition sidebarColumn,
        FrameworkElement sidebarPane,
        FrameworkElement outlinePanel,
        FrameworkElement pagesPanel,
        FrameworkElement searchPanel,
        FrameworkElement bookmarksPanel,
        ToggleButton outlineButton,
        ToggleButton pagesButton,
        ToggleButton searchButton,
        ToggleButton bookmarksButton,
        Button showRailButton,
        FrameworkElement annotationsPanel,
        ToggleButton annotationsButton)
    {
        _annotationsPanel = annotationsPanel;
        _annotationsButton = annotationsButton;
        _showRailButton = showRailButton;
        _sidebarColumn = sidebarColumn;
        _sidebarPane = sidebarPane;
        _outlinePanel = outlinePanel;
        _pagesPanel = pagesPanel;
        _searchPanel = searchPanel;
        _bookmarksPanel = bookmarksPanel;
        _outlineButton = outlineButton;
        _pagesButton = pagesButton;
        _searchButton = searchButton;
        _bookmarksButton = bookmarksButton;
    }

    public void Hide()
    {
        _showRailButton.Visibility = Visibility.Visible;
        _sidebarPane.Visibility = Visibility.Collapsed;
        _sidebarColumn.Width = new GridLength(0);
    }

    public void Show()
    {
        _showRailButton.Visibility = Visibility.Collapsed;
        _sidebarColumn.Width = new GridLength(_selectedWidth);
        _sidebarPane.Visibility = Visibility.Visible;
    }

    public void SelectOutline() => Select(_outlinePanel);

    public void SelectPages() => Select(_pagesPanel);

    public void SelectSearch() => Select(_searchPanel);

    public void SelectBookmarks() => Select(_bookmarksPanel);
    public void SelectAnnotations() => Select(_annotationsPanel);

    private void Select(FrameworkElement selected)
    {
        if (_applying)
        {
            return;
        }

        _applying = true;
        try
        {
            _selectedWidth = ReferenceEquals(selected, _pagesPanel) ? ThumbnailWidth : SidebarWidth;
            if (_sidebarPane.Visibility == Visibility.Visible)
            {
                _sidebarColumn.Width = new GridLength(_selectedWidth);
            }

            _outlinePanel.Visibility = ReferenceEquals(selected, _outlinePanel) ? Visibility.Visible : Visibility.Collapsed;
            _pagesPanel.Visibility = ReferenceEquals(selected, _pagesPanel) ? Visibility.Visible : Visibility.Collapsed;
            _searchPanel.Visibility = ReferenceEquals(selected, _searchPanel) ? Visibility.Visible : Visibility.Collapsed;
            _bookmarksPanel.Visibility = ReferenceEquals(selected, _bookmarksPanel) ? Visibility.Visible : Visibility.Collapsed;
            _outlineButton.IsChecked = ReferenceEquals(selected, _outlinePanel);
            _pagesButton.IsChecked = ReferenceEquals(selected, _pagesPanel);
            _searchButton.IsChecked = ReferenceEquals(selected, _searchPanel);
            _bookmarksButton.IsChecked = ReferenceEquals(selected, _bookmarksPanel);
            _annotationsPanel.Visibility = ReferenceEquals(selected, _annotationsPanel) ? Visibility.Visible : Visibility.Collapsed;
            _annotationsButton.IsChecked = ReferenceEquals(selected, _annotationsPanel);
        }
        finally
        {
            _applying = false;
        }
    }
}
