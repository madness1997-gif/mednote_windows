using MedNote.Core;
using Microsoft.UI.Xaml;

namespace MedNote.Windows.App;

public sealed partial class MainWindow
{
    private async void OnSplitWorkspaceClicked(object sender, RoutedEventArgs e) =>
        await ChangeWorkspaceModeAsync(WorkspaceMode.Split, focusTarget: false);

    private async void OnReaderWorkspaceClicked(object sender, RoutedEventArgs e) =>
        await ChangeWorkspaceModeAsync(WorkspaceMode.Reader, focusTarget: true);

    private async void OnNoteWorkspaceClicked(object sender, RoutedEventArgs e) =>
        await ChangeWorkspaceModeAsync(WorkspaceMode.Note, focusTarget: true);

    private async Task HandleF6Async()
    {
        if (_workspace is null || !NoteViewModel.IsReady)
        {
            return;
        }

        if (_workspace.Mode == WorkspaceMode.Split)
        {
            if (NoteWorkspacePane.ContainsFocus())
            {
                FocusReaderPane();
            }
            else
            {
                NoteWorkspacePane.FocusEditor();
            }

            return;
        }

        await ChangeWorkspaceModeAsync(
            _workspace.Mode == WorkspaceMode.Reader ? WorkspaceMode.Note : WorkspaceMode.Reader,
            focusTarget: true);
    }

    private async Task ChangeWorkspaceModeAsync(WorkspaceMode mode, bool focusTarget)
    {
        if (_workspace is null)
        {
            return;
        }

        if (_changingWorkspace)
        {
            _workspace.Apply(_workspace.Mode, _workspace.ReaderShare);
            return;
        }

        if (mode != WorkspaceMode.Reader && !NoteViewModel.IsReady)
        {
            _workspace.Apply(_workspace.Mode, _workspace.ReaderShare);
            return;
        }

        if (_workspace.Mode == mode)
        {
            _workspace.Apply(mode, _workspace.ReaderShare);
            if (focusTarget)
            {
                FocusWorkspace(mode);
            }

            return;
        }

        _changingWorkspace = true;
        try
        {
            var readerWasVisible = _workspace.Mode is WorkspaceMode.Reader or WorkspaceMode.Split;
            var noteWasVisible = _workspace.Mode is WorkspaceMode.Note or WorkspaceMode.Split;
            var readerWillBeVisible = mode is WorkspaceMode.Reader or WorkspaceMode.Split;
            var noteWillBeVisible = mode is WorkspaceMode.Note or WorkspaceMode.Split;
            if (readerWasVisible && !readerWillBeVisible)
            {
                _viewport.CaptureCurrentPosition();
            }

            if (noteWasVisible && !noteWillBeVisible)
            {
                await NoteWorkspacePane.FlushAsync();
            }

            _workspace.Apply(mode, _workspace.ReaderShare, notify: true);
            if (!readerWasVisible && readerWillBeVisible)
            {
                ApplyViewModelState();
                await _viewport.RestoreSavedPositionAsync();
            }

            if (focusTarget)
            {
                FocusWorkspace(mode);
            }
        }
        catch (Exception exception)
        {
            _workspace.Apply(_workspace.Mode, _workspace.ReaderShare);
            await ShowErrorAsync("Không chuyển được chế độ", exception.Message);
        }
        finally
        {
            _changingWorkspace = false;
        }
    }

    private void FocusWorkspace(WorkspaceMode mode)
    {
        if (mode == WorkspaceMode.Note)
        {
            NoteWorkspacePane.FocusEditor();
        }
        else
        {
            FocusReaderPane();
        }
    }

    private void FocusReaderPane()
    {
        if (!ViewModel.HasDocument)
        {
            ReaderPane.Focus(FocusState.Programmatic);
        }
        else if (ViewModel.ViewMode == PdfViewMode.Continuous)
        {
            ContinuousPagesList.Focus(FocusState.Programmatic);
        }
        else
        {
            SinglePageScrollViewer.Focus(FocusState.Programmatic);
        }
    }

    private void OnWorkspaceLayoutChanged(WorkspaceMode mode, double readerShare) =>
        _workspacePreferenceSave = SaveWorkspacePreferencesAsync(mode, readerShare);

    private async Task SaveWorkspacePreferencesAsync(WorkspaceMode mode, double readerShare)
    {
        try
        {
            await NoteViewModel.SaveWorkspacePreferencesAsync(mode, readerShare);
        }
        catch (Exception exception)
        {
            if (!_closing)
            {
                await ShowErrorAsync("Không lưu được bố cục", exception.Message);
            }
        }
    }

    private async void OnNoteOperationFailed(Exception exception) =>
        await ShowErrorAsync("Không cập nhật được Note", exception.Message);
}
