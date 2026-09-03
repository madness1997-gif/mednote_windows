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

    private void OnFitPageChecked(object sender, RoutedEventArgs e)
    {
        if (!IsApplyingControls)
        {
            ViewModel.SetFitMode(PdfFitMode.Page);
        }
    }

    private void OnFitWidthChecked(object sender, RoutedEventArgs e)
    {
        if (!IsApplyingControls)
        {
            ViewModel.SetFitMode(PdfFitMode.Width);
        }
    }

    private async void OnSingleModeChecked(object sender, RoutedEventArgs e)
    {
        if (IsApplyingControls)
        {
            return;
        }

        _viewport.CaptureCurrentPosition();
        ViewModel.SetViewMode(PdfViewMode.Single);
        await _viewport.RestoreSavedPositionAsync();
    }

    private async void OnContinuousModeChecked(object sender, RoutedEventArgs e)
    {
        if (IsApplyingControls)
        {
            return;
        }

        _viewport.CaptureCurrentPosition();
        ViewModel.SetViewMode(PdfViewMode.Continuous);
        await _viewport.RestoreSavedPositionAsync();
    }

    private void OnPanToolClicked(object sender, RoutedEventArgs e)
    {
        if (!IsApplyingControls)
        {
            ViewModel.SetActiveTool(PdfTool.Pan);
        }
    }

    private void OnSelectToolClicked(object sender, RoutedEventArgs e)
    {
        if (!IsApplyingControls)
        {
            ViewModel.SetActiveTool(PdfTool.Select);
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

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initializingControls)
        {
            _search?.Queue(SearchTextBox.Text);
        }
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

        if (FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox)
        {
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
