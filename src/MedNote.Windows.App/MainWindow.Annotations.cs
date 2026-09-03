using System.Globalization;
using MedNote.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MedNote.Windows.App;

public sealed partial class MainWindow
{
    private void OnPenToolClicked(object sender, RoutedEventArgs e) => SelectAnnotationTool(PdfTool.Pen);

    private void OnEraserToolClicked(object sender, RoutedEventArgs e) => SelectAnnotationTool(PdfTool.Eraser);

    private void OnHighlightToolClicked(object sender, RoutedEventArgs e) => SelectAnnotationTool(PdfTool.Highlight);

    private void OnAreaHighlightToolClicked(object sender, RoutedEventArgs e) => SelectAnnotationTool(PdfTool.AreaHighlight);

    private void OnUnderlineToolClicked(object sender, RoutedEventArgs e) => SelectAnnotationTool(PdfTool.Underline);

    private void OnStrikeoutToolClicked(object sender, RoutedEventArgs e) => SelectAnnotationTool(PdfTool.Strikeout);

    private void OnSquigglyToolClicked(object sender, RoutedEventArgs e) => SelectAnnotationTool(PdfTool.Squiggly);

    private void OnRectangleToolClicked(object sender, RoutedEventArgs e) => SelectAnnotationTool(PdfTool.Rectangle);

    private void OnEllipseToolClicked(object sender, RoutedEventArgs e) => SelectAnnotationTool(PdfTool.Ellipse);

    private void OnArrowToolClicked(object sender, RoutedEventArgs e) => SelectAnnotationTool(PdfTool.Arrow);

    private void OnCropToolClicked(object sender, RoutedEventArgs e) => SelectAnnotationTool(PdfTool.Crop);

    private void OnUndoAnnotationClicked(object sender, RoutedEventArgs e) => ViewModel.UndoAnnotations();

    private void OnRedoAnnotationClicked(object sender, RoutedEventArgs e) => ViewModel.RedoAnnotations();

    private void OnAnnotationColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string color })
        {
            return;
        }

        if (ViewModel.ActiveTool is PdfTool.Highlight or PdfTool.AreaHighlight)
        {
            ViewModel.SetHighlightColor(color);
        }
        else
        {
            ViewModel.SetInkColor(color);
        }
    }

    private void OnAnnotationWidthClicked(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string width }
            && double.TryParse(width, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            ViewModel.SetInkWidth(value);
        }
    }

    private async void OnExportClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasDocument)
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"{System.IO.Path.GetFileNameWithoutExtension(ViewModel.DocumentName)}-annotated",
        };
        picker.FileTypeChoices.Add("PDF", [".pdf"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null || string.IsNullOrWhiteSpace(file.Path))
        {
            return;
        }

        try
        {
            await ViewModel.ExportFlattenedAsync(file.Path);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Không xuất được PDF", exception.Message);
        }
    }

    private void SelectAnnotationTool(PdfTool tool)
    {
        if (!IsApplyingControls)
        {
            ViewModel.SetActiveTool(tool);
        }
    }
}
