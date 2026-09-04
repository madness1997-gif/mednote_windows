using MedNote.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace MedNote.Windows.App.Controllers;

/// <summary>
/// Owns Reader/Note visibility and the split ratio. Focus and viewport
/// restoration remain explicit MainWindow orchestration concerns.
/// </summary>
public sealed class WorkspaceLayoutController : IDisposable
{
    private readonly Grid _workspace;
    private readonly ColumnDefinition _readerColumn;
    private readonly ColumnDefinition _dividerColumn;
    private readonly ColumnDefinition _noteColumn;
    private readonly FrameworkElement _readerPane;
    private readonly FrameworkElement _divider;
    private readonly FrameworkElement _notePane;
    private readonly ToggleButton _splitButton;
    private readonly ToggleButton _readerButton;
    private readonly ToggleButton _noteButton;
    private bool _dragging;
    private bool _disposed;

    public WorkspaceLayoutController(
        Grid workspace,
        ColumnDefinition readerColumn,
        ColumnDefinition dividerColumn,
        ColumnDefinition noteColumn,
        FrameworkElement readerPane,
        FrameworkElement divider,
        FrameworkElement notePane,
        ToggleButton splitButton,
        ToggleButton readerButton,
        ToggleButton noteButton)
    {
        _workspace = workspace;
        _readerColumn = readerColumn;
        _dividerColumn = dividerColumn;
        _noteColumn = noteColumn;
        _readerPane = readerPane;
        _divider = divider;
        _notePane = notePane;
        _splitButton = splitButton;
        _readerButton = readerButton;
        _noteButton = noteButton;
        _divider.PointerPressed += OnDividerPointerPressed;
        _divider.PointerMoved += OnDividerPointerMoved;
        _divider.PointerReleased += OnDividerPointerReleased;
        _divider.PointerCanceled += OnDividerPointerReleased;
    }

    public event Action<WorkspaceMode, double>? LayoutChanged;

    public WorkspaceMode Mode { get; private set; } = WorkspaceMode.Reader;

    public double ReaderShare { get; private set; } = 50d;

    public void Apply(WorkspaceMode mode, double readerShare, bool notify = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReaderShare = Math.Clamp(readerShare, 20d, 80d);
        Mode = mode;
        switch (mode)
        {
            case WorkspaceMode.Split:
                _readerPane.Visibility = Visibility.Visible;
                _divider.Visibility = Visibility.Visible;
                _notePane.Visibility = Visibility.Visible;
                _readerColumn.Width = new GridLength(ReaderShare, GridUnitType.Star);
                _dividerColumn.Width = new GridLength(6);
                _noteColumn.Width = new GridLength(100d - ReaderShare, GridUnitType.Star);
                break;
            case WorkspaceMode.Note:
                _readerPane.Visibility = Visibility.Collapsed;
                _divider.Visibility = Visibility.Collapsed;
                _notePane.Visibility = Visibility.Visible;
                _readerColumn.Width = new GridLength(0);
                _dividerColumn.Width = new GridLength(0);
                _noteColumn.Width = new GridLength(1, GridUnitType.Star);
                break;
            default:
                _readerPane.Visibility = Visibility.Visible;
                _divider.Visibility = Visibility.Collapsed;
                _notePane.Visibility = Visibility.Collapsed;
                _readerColumn.Width = new GridLength(1, GridUnitType.Star);
                _dividerColumn.Width = new GridLength(0);
                _noteColumn.Width = new GridLength(0);
                break;
        }

        _splitButton.IsChecked = mode == WorkspaceMode.Split;
        _readerButton.IsChecked = mode == WorkspaceMode.Reader;
        _noteButton.IsChecked = mode == WorkspaceMode.Note;
        if (notify)
        {
            LayoutChanged?.Invoke(Mode, ReaderShare);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _divider.PointerPressed -= OnDividerPointerPressed;
        _divider.PointerMoved -= OnDividerPointerMoved;
        _divider.PointerReleased -= OnDividerPointerReleased;
        _divider.PointerCanceled -= OnDividerPointerReleased;
        if (_dragging)
        {
            _divider.ReleasePointerCaptures();
            _dragging = false;
        }
    }

    private void OnDividerPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Mode != WorkspaceMode.Split)
        {
            return;
        }

        _dragging = _divider.CapturePointer(e.Pointer);
        e.Handled = _dragging;
    }

    private void OnDividerPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || _workspace.ActualWidth <= 1d)
        {
            return;
        }

        ReaderShare = Math.Clamp(e.GetCurrentPoint(_workspace).Position.X / _workspace.ActualWidth * 100d, 20d, 80d);
        _readerColumn.Width = new GridLength(ReaderShare, GridUnitType.Star);
        _noteColumn.Width = new GridLength(100d - ReaderShare, GridUnitType.Star);
        e.Handled = true;
    }

    private void OnDividerPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _divider.ReleasePointerCapture(e.Pointer);
        _dragging = false;
        e.Handled = true;
        LayoutChanged?.Invoke(Mode, ReaderShare);
    }
}
