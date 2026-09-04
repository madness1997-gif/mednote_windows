using System.ComponentModel;
using System.Globalization;
using MedNote.Core;
using MedNote.Windows.App.Controllers;
using MedNote.Windows.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace MedNote.Windows.App.Controls;

public sealed partial class NotePane : UserControl
{
    private readonly SemaphoreSlim _navigationGate = new(1, 1);
    private NoteViewModel? _viewModel;
    private NoteEditorController? _editor;
    private bool _applyingNavigation;
    private bool _disposed;

    public NotePane()
    {
        InitializeComponent();
    }

    public event Action<Exception>? OperationFailed;

    public event Action<NoteSourceAnchorItem>? SourceRequested;

    public bool ContainsFocus()
    {
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (focused is not null)
        {
            if (ReferenceEquals(focused, this))
            {
                return true;
            }

            focused = VisualTreeHelper.GetParent(focused);
        }

        return false;
    }

    public void FocusEditor() => _editor?.FocusEditor();

    public void Attach(NoteViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (_viewModel is not null)
        {
            throw new InvalidOperationException("NotePane đã được gắn ViewModel.");
        }

        _viewModel = viewModel;
        DataContext = viewModel;
        _editor = new NoteEditorController(viewModel, Editor);
        _editor.SaveFailed += OnEditorSaveFailed;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public void LoadActiveSheet()
    {
        ApplyNavigationSelection();
        _editor?.LoadActiveSheet();
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_editor is not null)
        {
            _editor.CaptureSelection();
            await _editor.SaveNowAsync(cancellationToken);
        }
    }

    public async Task InsertPdfCropAsync(
        PdfCropResult crop,
        string documentId,
        string documentName,
        Func<CancellationToken, ValueTask> prepareSource,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_viewModel is null || _editor is null)
        {
            throw new InvalidOperationException("NotePane chưa sẵn sàng.");
        }

        ArgumentNullException.ThrowIfNull(prepareSource);
        await _navigationGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await prepareSource(cancellationToken);
            await _editor.InsertPdfCropAsync(
                crop,
                documentId,
                documentName,
                SelectedImageShare(),
                SelectedRowHeight(),
                cancellationToken);
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _navigationGate.WaitAsync();
        try
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (_editor is not null)
            {
                _editor.SaveFailed -= OnEditorSaveFailed;
                await _editor.DisposeAsync();
                _editor = null;
            }
        }
        finally
        {
            _navigationGate.Release();
            _navigationGate.Dispose();
        }
    }

    public async Task DisposeAfterFlushAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _navigationGate.WaitAsync(cancellationToken);
        try
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (_editor is not null)
            {
                _editor.SaveFailed -= OnEditorSaveFailed;
                _editor.DetachAfterFlush();
                _editor = null;
            }
        }
        finally
        {
            _navigationGate.Release();
            _navigationGate.Dispose();
        }
    }

    private async void OnNotebookSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_applyingNavigation && NotebookBox.SelectedItem is NotebookRecord notebook)
        {
            await NavigateAsync(() => _viewModel!.SelectNotebookAsync(notebook.Id));
        }
    }

    private async void OnSectionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_applyingNavigation && SectionBox.SelectedItem is SectionRecord section)
        {
            await NavigateAsync(() => _viewModel!.SelectSectionAsync(section.Id));
        }
    }

    private async void OnPageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_applyingNavigation && PageBox.SelectedItem is PageRecord page)
        {
            await NavigateAsync(() => _viewModel!.SelectPageAsync(page.Id));
        }
    }

    private async void OnSheetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_applyingNavigation && SheetBox.SelectedItem is NoteSheetNavigationItem sheet)
        {
            await NavigateAsync(() => _viewModel!.SelectSheetAsync(sheet.Id));
        }
    }

    private async void OnCreateNotebookClicked(object sender, RoutedEventArgs e) =>
        await NavigateAsync(() => _viewModel!.CreateNotebookAsync());

    private async void OnCreatePageClicked(object sender, RoutedEventArgs e) =>
        await NavigateAsync(() => _viewModel!.CreatePageAsync());

    private async void OnCreateSheetClicked(object sender, RoutedEventArgs e) =>
        await NavigateAsync(() => _viewModel!.CreateSheetAsync());

    private async void OnPageTitleLostFocus(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        try
        {
            await _viewModel.RenameActivePageAsync(PageTitleBox.Text);
        }
        catch (Exception exception)
        {
            PageTitleBox.Text = _viewModel.ActivePageTitle;
            OperationFailed?.Invoke(exception);
        }
    }

    private void OnBoldClicked(object sender, RoutedEventArgs e) => _editor?.ToggleBold();

    private void OnItalicClicked(object sender, RoutedEventArgs e) => _editor?.ToggleItalic();

    private void OnUnderlineClicked(object sender, RoutedEventArgs e) => _editor?.ToggleUnderline();

    private void OnBulletClicked(object sender, RoutedEventArgs e) => _editor?.ToggleBulletList();

    private void OnNumberedClicked(object sender, RoutedEventArgs e) => _editor?.ToggleNumberedList();

    private void OnFirstAidClicked(object sender, RoutedEventArgs e) =>
        _editor?.InsertFirstAid(SelectedImageShare(), SelectedRowHeight());

    private void OnInsertTableClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string value })
        {
            return;
        }

        var dimensions = value.Split('x');
        if (dimensions.Length == 2
            && int.TryParse(dimensions[0], NumberStyles.None, CultureInfo.InvariantCulture, out var rows)
            && int.TryParse(dimensions[1], NumberStyles.None, CultureInfo.InvariantCulture, out var columns))
        {
            _editor?.InsertBlankTable(rows, columns, SelectedColumnShare(), SelectedRowHeight());
        }
    }

    private void OnSourceClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is NoteSourceAnchorItem source)
        {
            SourceRequested?.Invoke(source);
        }
    }

    private void OnFontSizeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && float.TryParse(value, out var points))
        {
            _editor?.SetFontSize(points);
        }
    }

    private async Task NavigateAsync(Func<Task> navigation)
    {
        if (_disposed || _viewModel is null || _editor is null)
        {
            return;
        }

        await _navigationGate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            _editor.CaptureSelection();
            await _editor.SaveNowAsync();
            await navigation();
            LoadActiveSheet();
        }
        catch (Exception exception)
        {
            OperationFailed?.Invoke(exception);
            ApplyNavigationSelection();
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NoteViewModel.Notebooks)
            or nameof(NoteViewModel.Sections)
            or nameof(NoteViewModel.Pages)
            or nameof(NoteViewModel.Sheets)
            or nameof(NoteViewModel.Active))
        {
            ApplyNavigationSelection();
        }
    }

    private void ApplyNavigationSelection()
    {
        if (_viewModel is null)
        {
            return;
        }

        _applyingNavigation = true;
        try
        {
            NotebookBox.SelectedItem = _viewModel.Notebooks.FirstOrDefault(item => item.Id == _viewModel.Active.ActiveNotebookId);
            SectionBox.SelectedItem = _viewModel.Sections.FirstOrDefault(item => item.Id == _viewModel.Active.ActiveSectionId);
            PageBox.SelectedItem = _viewModel.Pages.FirstOrDefault(item => item.Id == _viewModel.Active.ActivePageId);
            SheetBox.SelectedItem = _viewModel.Sheets.FirstOrDefault(item => item.Id == _viewModel.Active.ActiveSheetId);
        }
        finally
        {
            _applyingNavigation = false;
        }
    }

    private void OnEditorSaveFailed(Exception exception) => OperationFailed?.Invoke(exception);

    private double SelectedColumnShare() => ReadSelectedNumber(LayoutRatioBox, 0.5d);

    private double SelectedImageShare() => ReadSelectedNumber(ImageWidthBox, 0.25d);

    private double SelectedRowHeight() => ReadSelectedNumber(RowHeightBox, 36d);

    private static double ReadSelectedNumber(ComboBox comboBox, double fallback)
    {
        return comboBox.SelectedItem is ComboBoxItem { Tag: string value }
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
    }
}
