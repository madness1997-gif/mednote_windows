using MedNote.Core;
using MedNote.Infrastructure;
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
    private readonly FileNoteRepository _noteRepository;
    private readonly JsonReaderLibraryStore _legacyReaderStore;
    private readonly ReaderLibraryCutoverStore _readerStore;
    private readonly ReaderViewportController _viewport;
    private readonly string? _startupDocumentPath;
    private ReaderWindowStateController? _state;
    private ReaderSidebarController? _sidebar;
    private ReaderSearchDebouncer? _search;
    private ReaderAnnotationController? _annotations;
    private WorkspaceLayoutController? _workspace;
    private Task _workspacePreferenceSave = Task.CompletedTask;
    private bool _initializingControls = true;
    private bool _initialized;
    private bool _changingWorkspace;
    private bool _closing;

    public MainWindow(string? startupDocumentPath = null)
    {
        _startupDocumentPath = startupDocumentPath;
        _noteRepository = new FileNoteRepository(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MedNote Reader",
            "native-library"));
        _legacyReaderStore = new JsonReaderLibraryStore();
        _readerStore = new ReaderLibraryCutoverStore(
            _legacyReaderStore,
            new NativeReaderLibraryStore(_noteRepository));
        NoteViewModel = new NoteViewModel(_noteRepository);
        ViewModel = new ReaderViewModel(new PdfiumPdfEngine(), _readerStore);
        InitializeComponent();
        _initializingControls = false;

        NoteWorkspacePane.Attach(NoteViewModel);
        NoteWorkspacePane.OperationFailed += OnNoteOperationFailed;
        NoteWorkspacePane.SourceRequested += OnNoteSourceRequested;
        ViewModel.CropCreated += OnReaderCropCreated;

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
        _workspace = new WorkspaceLayoutController(
            WorkspaceGrid,
            ReaderWorkspaceColumn,
            WorkspaceDividerColumn,
            NoteWorkspaceColumn,
            ReaderPane,
            WorkspaceDivider,
            NoteWorkspacePane,
            SplitWorkspaceButton,
            ReaderWorkspaceButton,
            NoteWorkspaceButton);
        _workspace.LayoutChanged += OnWorkspaceLayoutChanged;
        Closed += OnWindowClosed;
        ResizeWindow();
    }

    public ReaderViewModel ViewModel { get; }

    public NoteViewModel NoteViewModel { get; }

    private bool IsApplyingControls => _initializingControls || _state?.IsApplying == true;

    private async void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _viewport.Initialize();
        try
        {
            await NoteViewModel.InitializeAsync(_legacyReaderStore);
            NoteWorkspacePane.LoadActiveSheet();
            _readerStore.ActivateNative();
            _workspace?.Apply(
                NoteViewModel.Preferences.WorkspaceMode ?? WorkspaceMode.Reader,
                NoteViewModel.Preferences.ReaderShare);
        }
        catch (Exception exception)
        {
            _workspace?.Apply(WorkspaceMode.Reader, 50d);
            await ShowErrorAsync("Không khởi tạo được Note", exception.Message);
        }

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
        if (_workspace?.Mode != WorkspaceMode.Note)
        {
            await _viewport.RestoreSavedPositionAsync();
        }
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

                if (_workspace?.Mode == WorkspaceMode.Note)
                {
                    _workspace.Apply(WorkspaceMode.Split, _workspace.ReaderShare, notify: true);
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
        _closing = true;
        _search?.Dispose();
        _annotations?.Dispose();
        _state?.Dispose();
        if (_workspace is not null)
        {
            _workspace.LayoutChanged -= OnWorkspaceLayoutChanged;
            _workspace.Dispose();
        }

        _viewport.CaptureCurrentPosition();
        _viewport.Dispose();
        _sourceFocusCancellation?.Cancel();
        _sourceFocusCancellation?.Dispose();
        _sourceFocusPage?.SetSourceFocus(null);
        ViewModel.CropCreated -= OnReaderCropCreated;
        NoteWorkspacePane.SourceRequested -= OnNoteSourceRequested;
        NoteWorkspacePane.OperationFailed -= OnNoteOperationFailed;
        await NoteWorkspacePane.DisposeAsync();
        await _workspacePreferenceSave;
        await ViewModel.DisposeAsync();
        _legacyReaderStore.Dispose();
        await _noteRepository.DisposeAsync();
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
