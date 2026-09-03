using System.ComponentModel;
using MedNote.Core;
using MedNote.Windows.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MedNote.Windows.App.Controllers;

/// <summary>
/// Projects ReaderViewModel state onto WinUI chrome. It owns the temporary
/// "applying" guard used by ToggleButton/NumberBox event handlers so the shell
/// can stay a thin command router.
/// </summary>
public sealed class ReaderWindowStateController : IDisposable
{
    private readonly ReaderViewModel _viewModel;
    private readonly ReaderViewportController _viewport;
    private readonly FrameworkElement _emptyState;
    private readonly ListView _continuousPages;
    private readonly ScrollViewer _singlePage;
    private readonly ToggleButton _singleModeButton;
    private readonly ToggleButton _continuousModeButton;
    private readonly ToggleButton _fitPageButton;
    private readonly ToggleButton _fitWidthButton;
    private readonly ToggleButton _panToolButton;
    private readonly ToggleButton _selectToolButton;
    private readonly NumberBox _pageNumberBox;
    private readonly FontIcon _bookmarkIcon;
    private readonly Button _bookmarkButton;
    private readonly FrameworkElement _busyOverlay;
    private bool _disposed;

    public ReaderWindowStateController(
        ReaderViewModel viewModel,
        ReaderViewportController viewport,
        FrameworkElement emptyState,
        ListView continuousPages,
        ScrollViewer singlePage,
        ToggleButton singleModeButton,
        ToggleButton continuousModeButton,
        ToggleButton fitPageButton,
        ToggleButton fitWidthButton,
        ToggleButton panToolButton,
        ToggleButton selectToolButton,
        NumberBox pageNumberBox,
        FontIcon bookmarkIcon,
        Button bookmarkButton,
        FrameworkElement busyOverlay)
    {
        _viewModel = viewModel;
        _viewport = viewport;
        _emptyState = emptyState;
        _continuousPages = continuousPages;
        _singlePage = singlePage;
        _singleModeButton = singleModeButton;
        _continuousModeButton = continuousModeButton;
        _fitPageButton = fitPageButton;
        _fitWidthButton = fitWidthButton;
        _panToolButton = panToolButton;
        _selectToolButton = selectToolButton;
        _pageNumberBox = pageNumberBox;
        _bookmarkIcon = bookmarkIcon;
        _bookmarkButton = bookmarkButton;
        _busyOverlay = busyOverlay;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public bool IsApplying { get; private set; }

    public void ApplyAll()
    {
        ApplyFitMode();
        ApplyViewMode();
        ApplyActiveTool();
        UpdatePageControls();
        UpdateBookmarkButton();
        _busyOverlay.Visibility = _viewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    public void UpdatePageControls()
    {
        ApplyGuarded(() =>
        {
            _pageNumberBox.Maximum = Math.Max(1, _viewModel.PageCount);
            _pageNumberBox.Value = _viewModel.PageCount > 0 ? _viewModel.CurrentPage : 1;
        });
    }

    public void UpdateBookmarkButton()
    {
        var marked = _viewModel.Bookmarks.Contains(_viewModel.CurrentPage);
        _bookmarkIcon.Glyph = marked ? "\uE735" : "\uE734";
        ToolTipService.SetToolTip(
            _bookmarkButton,
            marked ? "Bỏ đánh dấu trang" : "Đánh dấu trang");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(ReaderViewModel.IsBusy):
                _busyOverlay.Visibility = _viewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;
                break;
            case nameof(ReaderViewModel.HasDocument):
                ApplyViewMode();
                break;
            case nameof(ReaderViewModel.CurrentPage):
            case nameof(ReaderViewModel.PageCount):
            case nameof(ReaderViewModel.Bookmarks):
                UpdatePageControls();
                UpdateBookmarkButton();
                break;
            case nameof(ReaderViewModel.FitMode):
                ApplyFitMode();
                break;
            case nameof(ReaderViewModel.ViewMode):
                ApplyViewMode();
                break;
            case nameof(ReaderViewModel.ActiveTool):
                ApplyActiveTool();
                break;
        }
    }

    private void ApplyFitMode() => ApplyGuarded(() =>
    {
        _fitPageButton.IsChecked = _viewModel.FitMode == PdfFitMode.Page;
        _fitWidthButton.IsChecked = _viewModel.FitMode == PdfFitMode.Width;
    });

    private void ApplyViewMode()
    {
        var continuous = _viewModel.HasDocument && _viewModel.ViewMode == PdfViewMode.Continuous;
        ApplyGuarded(() =>
        {
            _emptyState.Visibility = _viewModel.HasDocument ? Visibility.Collapsed : Visibility.Visible;
            _continuousPages.Visibility = continuous ? Visibility.Visible : Visibility.Collapsed;
            _singlePage.Visibility = _viewModel.HasDocument && !continuous ? Visibility.Visible : Visibility.Collapsed;
            _singleModeButton.IsChecked = !continuous;
            _continuousModeButton.IsChecked = continuous;
        });

        if (continuous)
        {
            _viewport.OnViewModeApplied();
        }
    }

    private void ApplyActiveTool() => ApplyGuarded(() =>
    {
        _panToolButton.IsChecked = _viewModel.ActiveTool == PdfTool.Pan;
        _selectToolButton.IsChecked = _viewModel.ActiveTool == PdfTool.Select;
    });

    private void ApplyGuarded(Action action)
    {
        if (IsApplying)
        {
            action();
            return;
        }

        IsApplying = true;
        try
        {
            action();
        }
        finally
        {
            IsApplying = false;
        }
    }
}
