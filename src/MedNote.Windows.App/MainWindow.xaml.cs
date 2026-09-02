using System.ComponentModel;
using MedNote.Core;
using MedNote.Windows.App.Controllers;
using MedNote.Windows.App.Infrastructure;
using MedNote.Windows.App.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;

namespace MedNote.Windows.App;

public sealed partial class MainWindow : Window
{
    private readonly ReaderViewportController _viewport;
    private readonly string? _startupDocumentPath;
    private bool _updatingControls;
    private bool _initialized;

    public MainWindow(string? startupDocumentPath = null)
    {
        _startupDocumentPath = startupDocumentPath;
        ViewModel = new ReaderViewModel(new PdfiumPdfEngine(), new JsonReaderLibraryStore());
        _updatingControls = true;
        InitializeComponent();
        _updatingControls = false;
        _viewport = new ReaderViewportController(
            ViewModel,
            ReaderSurface,
            ContinuousPagesList,
            SinglePageScrollViewer,
            DispatcherQueue);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnWindowClosed;
        ResizeWindow();
    }

    public ReaderViewModel ViewModel { get; }

    private async void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _viewport.Initialize();
        var hasStartupDocument = !string.IsNullOrWhiteSpace(_startupDocumentPath)
            && File.Exists(_startupDocumentPath);
        await ViewModel.InitializeAsync(reopenActiveDocument: !hasStartupDocument);
        if (hasStartupDocument)
        {
            await OpenFileAsync(_startupDocumentPath!);
            return;
        }

        if (!string.IsNullOrWhiteSpace(ViewModel.PendingPasswordDocumentPath))
        {
            await OpenFileAsync(ViewModel.PendingPasswordDocumentPath);
            return;
        }

        ApplyViewModelState();
        await _viewport.RestoreSavedPositionAsync();
    }

    private async void OnOpenClicked(object sender, RoutedEventArgs e) => await PickAndOpenFileAsync();

    private async Task PickAndOpenFileAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".pdf");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            await OpenFileAsync(file.Path);
        }
    }

    private async Task OpenFileAsync(string path)
    {
        var password = RenderProbe.StartupPassword;
        while (true)
        {
            try
            {
                await ViewModel.OpenDocumentAsync(path, password);
                if (RenderProbe.StartupRotation is { } startupRotation)
                {
                    ViewModel.SetRotation(startupRotation);
                }

                var soakSequence = RenderProbe.TargetSequence(ViewModel.PageCount);
                if (soakSequence.Count > 0)
                {
                    ViewModel.SetViewMode(PdfViewMode.Single);
                }

                ApplyViewModelState();
                if (soakSequence.Count > 0)
                {
                    await RunRenderSoakAsync(soakSequence);
                }
                else if (RenderProbe.TargetPage(ViewModel.PageCount) is { } probePage)
                {
                    NavigateToPage(probePage, true);
                }
                else
                {
                    await _viewport.RestoreSavedPositionAsync();
                }

                return;
            }
            catch (PdfPasswordRequiredException)
            {
                password = await PromptForPdfPasswordAsync();
                if (password is null)
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                await ShowErrorAsync("Không mở được PDF", exception.Message);
                return;
            }
        }
    }

    private async Task RunRenderSoakAsync(IReadOnlyList<int> sequence)
    {
        var snapshots = new List<RenderProbeSnapshot>(sequence.Count);
        try
        {
            foreach (var targetPage in sequence)
            {
                RenderProbe.BeginSoakTarget(targetPage);
                NavigateToPage(targetPage, true);
                Direct2DPageSurfaceFactory.RequestSurfaceRecreation();
                snapshots.Add(await RenderProbe.WaitForSoakTargetAsync(TimeSpan.FromSeconds(20)));
            }

            RenderProbe.CompleteSoak(ViewModel.PageCount, snapshots);
        }
        catch (Exception exception)
        {
            RenderProbe.FailSoak(exception);
        }
    }

    private async Task<string?> PromptForPdfPasswordAsync()
    {
        var passwordBox = new PasswordBox
        {
            PlaceholderText = "Nhập mật khẩu PDF",
        };
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "PDF được bảo vệ",
            Content = passwordBox,
            PrimaryButtonText = "Mở",
            CloseButtonText = "Hủy",
            DefaultButton = ContentDialogButton.Primary,
        };
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? passwordBox.Password : null;
    }

    private void OnPreviousPageClicked(object sender, RoutedEventArgs e) => NavigateToPage(ViewModel.CurrentPage - 1);

    private void OnNextPageClicked(object sender, RoutedEventArgs e) => NavigateToPage(ViewModel.CurrentPage + 1);

    private void OnPageNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_updatingControls || double.IsNaN(args.NewValue))
        {
            return;
        }

        NavigateToPage(checked((int)Math.Round(args.NewValue)));
    }

    private void NavigateToPage(int requestedPage, bool disableAnimation = false)
    {
        _viewport.NavigateToPage(requestedPage, disableAnimation);
        UpdatePageControls();
    }

    private void OnZoomOutClicked(object sender, RoutedEventArgs e) => ViewModel.StepZoom(-1);

    private void OnZoomInClicked(object sender, RoutedEventArgs e) => ViewModel.StepZoom(1);

    private void OnFitPageChecked(object sender, RoutedEventArgs e)
    {
        if (_updatingControls)
        {
            return;
        }

        ViewModel.SetFitMode(PdfFitMode.Page);
        ApplyFitMode();
    }

    private void OnFitWidthChecked(object sender, RoutedEventArgs e)
    {
        if (_updatingControls)
        {
            return;
        }

        ViewModel.SetFitMode(PdfFitMode.Width);
        ApplyFitMode();
    }

    private async void OnSingleModeChecked(object sender, RoutedEventArgs e)
    {
        if (_updatingControls)
        {
            return;
        }

        _viewport.CaptureCurrentPosition();
        ViewModel.SetViewMode(PdfViewMode.Single);
        ApplyViewMode();
        await _viewport.RestoreSavedPositionAsync();
    }

    private async void OnContinuousModeChecked(object sender, RoutedEventArgs e)
    {
        if (_updatingControls)
        {
            return;
        }

        _viewport.CaptureCurrentPosition();
        ViewModel.SetViewMode(PdfViewMode.Continuous);
        ApplyViewMode();
        await _viewport.RestoreSavedPositionAsync();
    }

    private void OnPanToolChecked(object sender, RoutedEventArgs e)
    {
        if (_updatingControls)
        {
            return;
        }

        ViewModel.SetActiveTool(PdfTool.Pan);
        _updatingControls = true;
        PanToolButton.IsChecked = true;
        _updatingControls = false;
    }

    private void OnBookmarkClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleBookmark();
        UpdateBookmarkButton();
    }

    private void OnSidebarPageClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PdfPageViewModel page)
        {
            NavigateToPage(page.Number);
        }
    }

    private void OnBookmarkItemClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is int page)
        {
            NavigateToPage(page);
        }
    }

    private void OnOutlineTabChecked(object sender, RoutedEventArgs e) => SelectSidebarTab(OutlinePanel);

    private void OnPagesTabChecked(object sender, RoutedEventArgs e) => SelectSidebarTab(SidebarPagesList);

    private void OnSearchTabChecked(object sender, RoutedEventArgs e) => SelectSidebarTab(SearchPanel);

    private void OnBookmarksTabChecked(object sender, RoutedEventArgs e) => SelectSidebarTab(BookmarksPanel);

    private void OnHideRailClicked(object sender, RoutedEventArgs e)
    {
        SidebarPane.Visibility = Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(0);
    }

    private void OnShowRailClicked(object sender, RoutedEventArgs e)
    {
        SidebarColumn.Width = new GridLength(264);
        SidebarPane.Visibility = Visibility.Visible;
    }

    private void SelectSidebarTab(UIElement selected)
    {
        if (_updatingControls)
        {
            return;
        }

        _updatingControls = true;
        OutlinePanel.Visibility = selected == OutlinePanel ? Visibility.Visible : Visibility.Collapsed;
        SidebarPagesList.Visibility = selected == SidebarPagesList ? Visibility.Visible : Visibility.Collapsed;
        SearchPanel.Visibility = selected == SearchPanel ? Visibility.Visible : Visibility.Collapsed;
        BookmarksPanel.Visibility = selected == BookmarksPanel ? Visibility.Visible : Visibility.Collapsed;
        OutlineTabButton.IsChecked = selected == OutlinePanel;
        PagesTabButton.IsChecked = selected == SidebarPagesList;
        SearchTabButton.IsChecked = selected == SearchPanel;
        BookmarksTabButton.IsChecked = selected == BookmarksPanel;
        _updatingControls = false;
    }

    private async void OnReaderDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        var pdf = items.OfType<StorageFile>().FirstOrDefault(file => file.FileType.Equals(".pdf", StringComparison.OrdinalIgnoreCase));
        if (pdf is not null)
        {
            await OpenFileAsync(pdf.Path);
        }
    }

    private void OnReaderDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Mở PDF";
        e.DragUIOverride.IsCaptionVisible = true;
    }

    private async void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var controlDown = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;
        if (controlDown && e.Key == VirtualKey.O)
        {
            e.Handled = true;
            await PickAndOpenFileAsync();
            return;
        }

        if (e.Key is VirtualKey.PageDown or VirtualKey.Right)
        {
            NavigateToPage(ViewModel.CurrentPage + 1);
            e.Handled = true;
        }
        else if (e.Key is VirtualKey.PageUp or VirtualKey.Left)
        {
            NavigateToPage(ViewModel.CurrentPage - 1);
            e.Handled = true;
        }
        else if (controlDown && e.Key == VirtualKey.Add)
        {
            ViewModel.StepZoom(1);
            e.Handled = true;
        }
        else if (controlDown && e.Key == VirtualKey.Subtract)
        {
            ViewModel.StepZoom(-1);
            e.Handled = true;
        }

        await Task.CompletedTask;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ReaderViewModel.IsBusy):
                BusyOverlay.Visibility = ViewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;
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
        }
    }

    private void ApplyViewModelState()
    {
        ApplyFitMode();
        ApplyViewMode();
        UpdatePageControls();
        UpdateBookmarkButton();
        BusyOverlay.Visibility = ViewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        if (Root.XamlRoot is not null)
        {
            ViewModel.SetViewport(ReaderSurface.ActualWidth, ReaderSurface.ActualHeight, Root.XamlRoot.RasterizationScale);
        }
    }

    private void ApplyFitMode()
    {
        _updatingControls = true;
        FitPageButton.IsChecked = ViewModel.FitMode == PdfFitMode.Page;
        FitWidthButton.IsChecked = ViewModel.FitMode == PdfFitMode.Width;
        _updatingControls = false;
    }

    private void ApplyViewMode()
    {
        _updatingControls = true;
        var hasDocument = ViewModel.HasDocument;
        var continuous = hasDocument && ViewModel.ViewMode == PdfViewMode.Continuous;
        EmptyState.Visibility = hasDocument ? Visibility.Collapsed : Visibility.Visible;
        ContinuousPagesList.Visibility = continuous ? Visibility.Visible : Visibility.Collapsed;
        SinglePageScrollViewer.Visibility = hasDocument && !continuous ? Visibility.Visible : Visibility.Collapsed;
        SingleModeButton.IsChecked = !continuous;
        ContinuousModeButton.IsChecked = continuous;
        _updatingControls = false;

        if (continuous)
        {
            _viewport.OnViewModeApplied();
        }
    }

    private void UpdatePageControls()
    {
        _updatingControls = true;
        PageNumberBox.Maximum = Math.Max(1, ViewModel.PageCount);
        PageNumberBox.Value = ViewModel.PageCount > 0 ? ViewModel.CurrentPage : 1;
        _updatingControls = false;
    }

    private void UpdateBookmarkButton()
    {
        var marked = ViewModel.Bookmarks.Contains(ViewModel.CurrentPage);
        BookmarkIcon.Glyph = marked ? "\uE735" : "\uE734";
        ToolTipService.SetToolTip(BookmarkButton, marked ? "Bỏ đánh dấu trang" : "Đánh dấu trang");
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _viewport.CaptureCurrentPosition();
        _viewport.Dispose();
        await ViewModel.DisposeAsync();
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "Đóng",
        };
        await dialog.ShowAsync();
    }

    private void ResizeWindow()
    {
        var windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        AppWindow.GetFromWindowId(windowId).Resize(new SizeInt32(1_420, 900));
    }

}
