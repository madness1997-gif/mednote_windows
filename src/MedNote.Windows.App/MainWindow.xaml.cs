using MedNote.Core;
using MedNote.Windows.App.Controllers;
using MedNote.Windows.App.Infrastructure;
using MedNote.Windows.App.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MedNote.Windows.App;

public sealed partial class MainWindow : Window
{
    private readonly ReaderViewportController _viewport;
    private readonly string? _startupDocumentPath;
    private ReaderWindowStateController? _state;
    private ReaderSidebarController? _sidebar;
    private ReaderSearchDebouncer? _search;
    private ReaderAnnotationController? _annotations;
    private bool _initializingControls = true;
    private bool _initialized;

    public MainWindow(string? startupDocumentPath = null)
    {
        _startupDocumentPath = startupDocumentPath;
        ViewModel = new ReaderViewModel(new PdfiumPdfEngine(), new JsonReaderLibraryStore());
        InitializeComponent();
        _initializingControls = false;

        _viewport = new ReaderViewportController(
            ViewModel,
            ReaderSurface,
            ContinuousPagesList,
            SinglePageScrollViewer,
            DispatcherQueue);
        _state = new ReaderWindowStateController(
            ViewModel,
            _viewport,
            EmptyState,
            ContinuousPagesList,
            SinglePageScrollViewer,
            SingleModeButton,
            ContinuousModeButton,
            FitPageButton,
            FitWidthButton,
            PanToolButton,
            SelectToolButton,
            PageNumberBox,
            BookmarkIcon,
            BookmarkButton,
            BusyOverlay);
        _sidebar = new ReaderSidebarController(
            SidebarColumn,
            SidebarPane,
            OutlinePanel,
            SidebarPagesList,
            SearchPanel,
            BookmarksPanel,
            OutlineTabButton,
            PagesTabButton,
            SearchTabButton,
            BookmarksTabButton);
        _search = new ReaderSearchDebouncer(ViewModel, DispatcherQueue);
        _annotations = new ReaderAnnotationController(
            ViewModel,
            new Dictionary<PdfTool, Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>
            {
                [PdfTool.Pen] = PenToolButton,
                [PdfTool.Eraser] = EraserToolButton,
                [PdfTool.Highlight] = HighlightToolButton,
                [PdfTool.AreaHighlight] = AreaHighlightToolButton,
                [PdfTool.Underline] = UnderlineToolButton,
                [PdfTool.Strikeout] = StrikeoutToolButton,
                [PdfTool.Squiggly] = SquigglyToolButton,
                [PdfTool.Rectangle] = RectangleToolButton,
                [PdfTool.Ellipse] = EllipseToolButton,
                [PdfTool.Arrow] = ArrowToolButton,
                [PdfTool.Crop] = CropToolButton,
            },
            UndoAnnotationButton,
            RedoAnnotationButton,
            ExportPdfButton);
        Closed += OnWindowClosed;
        ResizeWindow();
    }

    public ReaderViewModel ViewModel { get; }

    private bool IsApplyingControls => _initializingControls || _state?.IsApplying == true;

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
                if (SearchTextBox.Text.Length > 0)
                {
                    SearchTextBox.Text = string.Empty;
                    _search?.Reset();
                }

                if (RenderProbe.StartupRotation is { } startupRotation)
                {
                    ViewModel.SetRotation(startupRotation);
                }

                ApplyViewModelState();
                var probePage = RenderProbe.TargetPage(ViewModel.PageCount);
                if (probePage is not null)
                {
                    NavigateToPage(probePage.Value, true);
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

    private void ApplyViewModelState()
    {
        _state?.ApplyAll();
        if (Root.XamlRoot is not null)
        {
            ViewModel.SetViewport(
                ReaderSurface.ActualWidth,
                ReaderSurface.ActualHeight,
                Root.XamlRoot.RasterizationScale);
        }
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _search?.Dispose();
        _annotations?.Dispose();
        _state?.Dispose();
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
