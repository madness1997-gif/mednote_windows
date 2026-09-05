using System.ComponentModel;
using MedNote.Core;
using MedNote.Windows.App.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MedNote.Windows.App;

public sealed partial class MainWindow
{
    private void OnAnnotationsTabChecked(object sender, RoutedEventArgs e)
    {
        if (_initializingControls) return;
        _sidebar?.SelectAnnotations();
        RefreshAnnotationList();
    }

    private void OnAnnotationFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializingControls) RefreshAnnotationList();
    }

    private void OnAnnotationListStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReaderViewModel.Annotations) or nameof(ReaderViewModel.CurrentPage)
            or nameof(ReaderViewModel.HasDocument)) RefreshAnnotationList();
    }

    private void RefreshAnnotationList()
    {
        if (_initializingControls || AnnotationsPanel.Visibility != Visibility.Visible) return;
        AnnotationItems.Children.Clear();
        var items = ViewModel.Annotations
            .Where(item => AnnotationFilter.SelectedIndex != 1 || item.Page == ViewModel.CurrentPage)
            .OrderBy(item => item.Page).ThenBy(item => item.CreatedAt).ToArray();
        AnnotationListStatus.Text = items.Length == 0 ? "Chưa có chú thích trong phạm vi này." : $"{items.Length} chú thích";
        foreach (var annotation in items)
        {
            var row = new Grid { ColumnSpacing = 4 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = annotation.Kind switch
            {
                PdfAnnotationKind.Highlight => "Tô chữ", PdfAnnotationKind.AreaHighlight => "Tô vùng",
                PdfAnnotationKind.Underline => "Gạch chân", PdfAnnotationKind.Strikeout => "Gạch ngang",
                PdfAnnotationKind.Squiggly => "Gạch lượn", PdfAnnotationKind.Ink => "Bút",
                PdfAnnotationKind.Rectangle => "Hình chữ nhật", PdfAnnotationKind.Ellipse => "Hình elip",
                PdfAnnotationKind.Arrow => "Mũi tên", _ => "Chú thích",
            };
            var text = new StackPanel { Spacing = 3 };
            text.Children.Add(new TextBlock { Text = $"Trang {annotation.Page} · {label}", FontSize = 11, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrWhiteSpace(annotation.Text))
                text.Children.Add(new TextBlock { Text = annotation.Text, FontSize = 11, MaxLines = 3,
                    TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis });
            var open = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(7),
                BorderBrush = new SolidColorBrush(ParseAnnotationColor(annotation.Color)), BorderThickness = new Thickness(3, 0, 0, 0) };
            open.Click += async (_, _) =>
            {
                NavigateToPage(annotation.Page);
                var rect = annotation.Rect ?? annotation.Rects?.FirstOrDefault();
                ShowSourceFocus(annotation.Page, rect);
                if (rect is not null) await _viewport.NavigateToSourceAsync(annotation.Page, rect);
                FocusReaderPane();
            };
            var delete = new Button { Content = "×", Padding = new Thickness(7), VerticalAlignment = VerticalAlignment.Top };
            ToolTipService.SetToolTip(delete, "Xóa chú thích — có thể hoàn tác");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(delete, $"Xóa {label}, trang {annotation.Page}");
            delete.Click += (_, _) => ViewModel.DeleteAnnotations([annotation.Id]);
            Grid.SetColumn(delete, 1);
            row.Children.Add(open);
            row.Children.Add(delete);
            AnnotationItems.Children.Add(row);
        }
    }

    private static Windows.UI.Color ParseAnnotationColor(string value)
    {
        var normalized = PdfAnnotationColor.Normalize(value);
        return ColorHelper.FromArgb(255, Convert.ToByte(normalized.Substring(1, 2), 16),
            Convert.ToByte(normalized.Substring(3, 2), 16), Convert.ToByte(normalized.Substring(5, 2), 16));
    }

    private async void OnCustomAnnotationColorClicked(object sender, RoutedEventArgs e)
    {
        var highlight = ViewModel.ActiveTool is PdfTool.Highlight or PdfTool.AreaHighlight;
        var picker = new ColorPicker { IsAlphaEnabled = false,
            Color = ParseAnnotationColor(highlight ? ViewModel.HighlightColor : ViewModel.InkColor),
            IsHexInputVisible = true, IsColorSliderVisible = true };
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Màu chú thích", Content = picker,
            PrimaryButtonText = "Áp dụng", CloseButtonText = "Hủy", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var color = $"#{picker.Color.R:x2}{picker.Color.G:x2}{picker.Color.B:x2}";
        if (highlight) ViewModel.SetHighlightColor(color); else ViewModel.SetInkColor(color);
    }
}
