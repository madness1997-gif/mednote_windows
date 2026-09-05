using MedNote.Core;
using MedNote.Windows.App.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;

namespace MedNote.Windows.App;

public sealed partial class MainWindow
{
    private void OnPreviousPageClicked(object sender, RoutedEventArgs e) => NavigateToPage(ViewModel.CurrentPage - 1);

    private void OnNextPageClicked(object sender, RoutedEventArgs e) => NavigateToPage(ViewModel.CurrentPage + 1);

    private void OnPageNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (IsApplyingControls || double.IsNaN(args.NewValue))
        {
            return;
        }

        NavigateToPage(checked((int)Math.Round(args.NewValue)));
    }

    private void NavigateToPage(int requestedPage, bool disableAnimation = false)
    {
        _viewport.NavigateToPage(requestedPage, disableAnimation);
        _state?.UpdatePageControls();
    }

    private void OnZoomOutClicked(object sender, RoutedEventArgs e) => ViewModel.StepZoom(-1);

    private void OnZoomInClicked(object sender, RoutedEventArgs e) => ViewModel.StepZoom(1);

    private bool _changingReaderDisplay;

    private void OnReaderViewOpening(object sender, object e)
    {
        _state?.RefreshDisplayOptions();
        ReaderOnlyViewButton.Content = _workspace?.Mode == WorkspaceMode.Reader
            ? "Trở lại cả hai" : "Chỉ Reader";
    }

    private async void OnFitPageChecked(object sender, RoutedEventArgs e) =>
        await ApplyReaderDisplayChangeAsync(() =>
        {
            ViewModel.SetFitMode(PdfFitMode.Page);
            ViewModel.SetZoom(1d);
        });

    private async void OnFitWidthChecked(object sender, RoutedEventArgs e) =>
        await ApplyReaderDisplayChangeAsync(() =>
        {
            ViewModel.SetFitMode(PdfFitMode.Width);
            ViewModel.SetZoom(1d);
        });

    private async void OnSingleModeChecked(object sender, RoutedEventArgs e) =>
        await ApplyReaderDisplayChangeAsync(() =>
        {
            ViewModel.SetViewMode(PdfViewMode.Single);
            ViewModel.SetFitMode(PdfFitMode.Page);
            ViewModel.SetZoom(1d);
        });

    private async void OnContinuousModeChecked(object sender, RoutedEventArgs e) =>
        await ApplyReaderDisplayChangeAsync(() =>
        {
            ViewModel.SetViewMode(PdfViewMode.Continuous);
            ViewModel.SetFitMode(PdfFitMode.Width);
            ViewModel.SetZoom(1d);
        });

    private async void OnRotateReaderClicked(object sender, RoutedEventArgs e) =>
        await ApplyReaderDisplayChangeAsync(() => ViewModel.SetRotation(ViewModel.Rotation + 90));

    private async void OnToggleReaderWorkspaceClicked(object sender, RoutedEventArgs e)
    {
        ReaderViewFlyout.Hide();
        await ChangeWorkspaceModeAsync(
            _workspace?.Mode == WorkspaceMode.Reader ? WorkspaceMode.Split : WorkspaceMode.Reader,
            focusTarget: true);
    }

    private async Task ApplyReaderDisplayChangeAsync(Action change)
    {
        if (IsApplyingControls || _changingReaderDisplay || !ViewModel.HasDocument)
        {
            _state?.RefreshDisplayOptions();
            return;
        }

        _changingReaderDisplay = true;
        ReaderViewFlyout.Hide();
        try
        {
            _viewport.CaptureCurrentPosition();
            change();
            _state?.RefreshDisplayOptions();
            await _viewport.RestoreSavedPositionAsync();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Không đổi được hiển thị PDF", exception.Message);
        }
        finally
        {
            _changingReaderDisplay = false;
        }
    }

    private void OnPanToolClicked(object sender, RoutedEventArgs e)
    {
        if (!IsApplyingControls)
        {
            ViewModel.SetActiveTool(PdfTool.Pan);
            _annotations?.Apply();
        }
    }

    private void OnSelectToolClicked(object sender, RoutedEventArgs e)
    {
        if (!IsApplyingControls)
        {
            ViewModel.SetActiveTool(PdfTool.Select);
            _annotations?.Apply();
        }
    }

    private void OnBookmarkClicked(object sender, RoutedEventArgs e) => ViewModel.ToggleBookmark();

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

    private async void OnSearchResultClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PdfSearchMatch match
            || match.PageIndex < 0
            || match.PageIndex >= ViewModel.Pages.Count)
        {
            return;
        }

        NavigateToPage(match.PageNumber, true);
        try
        {
            await ViewModel.Pages[match.PageIndex].SelectTextRangeAsync(
                match.StartIndex,
                match.Length);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Không chọn được kết quả", exception.Message);
        }
    }

    private async void OnSearchSubmitClicked(object sender, RoutedEventArgs e)
    {
        if (!_initializingControls) await ViewModel.SearchAsync(SearchTextBox.Text);
    }

    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        await ViewModel.SearchAsync(SearchTextBox.Text);
    }

    private async void OnReaderDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        var pdf = items.OfType<StorageFile>().FirstOrDefault(
            file => file.FileType.Equals(".pdf", StringComparison.OrdinalIgnoreCase));
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

        if (e.Key == VirtualKey.F6)
        {
            e.Handled = true;
            await HandleF6Async();
            return;
        }

        if (controlDown && e.Key == VirtualKey.F && !NoteWorkspacePane.ContainsFocus())
        {
            _sidebar?.SelectSearch();
            _sidebar?.Show();
            SearchTextBox.Focus(FocusState.Programmatic);
            SearchTextBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox or RichEditBox
            || NoteWorkspacePane.ContainsFocus())
        {
            return;
        }

        if (e.Key == VirtualKey.Space)
        {
            e.Handled = true;
            return;
        }

        if (controlDown && e.Key == VirtualKey.Z)
        {
            e.Handled = ViewModel.UndoAnnotations();
            return;
        }

        if (controlDown && e.Key == VirtualKey.Y)
        {
            e.Handled = ViewModel.RedoAnnotations();
            return;
        }

        if (controlDown && e.Key == VirtualKey.C && CopySelectedText())
        {
            e.Handled = true;
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

    private bool CopySelectedText()
    {
        if (!ViewModel.HasTextSelection)
        {
            return false;
        }

        var package = new DataPackage();
        package.SetText(ViewModel.SelectedText);
        Clipboard.SetContent(package);
        Clipboard.Flush();
        return true;
    }
}
