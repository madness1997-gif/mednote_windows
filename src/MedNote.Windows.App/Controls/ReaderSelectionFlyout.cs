using MedNote.Core;
using MedNote.Infrastructure;
using MedNote.Windows.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;

namespace MedNote.Windows.App.Controls;

/// <summary>One selection-bound popup per realized page; closing cancels network/audio work.</summary>
internal sealed class ReaderSelectionFlyout
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly Flyout _flyout = new() { Placement = FlyoutPlacementMode.Top };
    private readonly StackPanel _body = new() { Spacing = 8, Width = 350 };
    private readonly StackPanel _translation = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private CancellationTokenSource? _lookup;
    private MediaPlayer? _audio;
    private PdfPageViewModel? _page;
    private PdfTextSelection? _selection;

    public ReaderSelectionFlyout()
    {
        _flyout.Content = new ScrollViewer { Content = _body, MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _flyout.Closed += (_, _) => Cancel();
    }

    public void Hide()
    {
        Cancel();
        _flyout.Hide();
    }

    public void Show(FrameworkElement target, PdfPageViewModel page, Point position, bool translate = false)
    {
        Hide();
        if (page.Selection is not { Length: > 0 } selection) return;
        _page = page;
        _selection = selection;
        _body.Width = Math.Clamp(target.XamlRoot.Size.Width - 48d, 220d, 350d);
        _body.Children.Clear();
        _translation.Children.Clear();
        _translation.Visibility = Visibility.Collapsed;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        Add(row, "Chép", () => Copy(selection.Text));
        Add(row, "Dịch", () => _ = TranslateAsync());
        Add(row, "Note", () => { if (Current) page.SendSelectionToNote(); Hide(); });
        Add(row, "Oxford", () => _ = OpenOxfordAsync(selection.Text));
        Add(row, "Đóng", Hide);
        _body.Children.Add(row);
        var marks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        foreach (var (label, kind) in new[] { ("Tô", PdfAnnotationKind.Highlight), ("Chân", PdfAnnotationKind.Underline),
            ("Ngang", PdfAnnotationKind.Strikeout), ("Lượn", PdfAnnotationKind.Squiggly) })
            Add(marks, label, () => { if (Current) page.AddSelectionMarkup(kind); Hide(); });
        _body.Children.Add(marks);
        _body.Children.Add(_translation);
        _flyout.ShowAt(target, new FlyoutShowOptions { Position = position });
        if (translate) _ = TranslateAsync();
    }

    private bool Current => _page is not null && ReferenceEquals(_page.Selection, _selection);

    private async Task TranslateAsync()
    {
        if (!Current || _selection is not { } selection) return;
        _lookup?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _lookup = cancellation;
        _translation.Visibility = Visibility.Visible;
        _translation.Children.Clear();
        Text(_translation, selection.Text);
        var status = Text(_translation, "Đang tìm nghĩa và gợi ý dịch…");
        try
        {
            var result = await new ReaderDictionaryService(Http).LookupAsync(selection.Text, cancellation.Token);
            if (cancellation.IsCancellationRequested || !Current) return;
            status.Text = result.Translation ?? result.Error ?? "Không tìm thấy bản dịch.";
            if (result.Alternatives.Count > 0) Text(_translation, "Khác: " + string.Join(" · ", result.Alternatives));
            if (result.Phonetic is { } phonetic) Text(_translation, phonetic);
            if (result.Translation is { } translated)
            {
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
                Add(actions, "Chép bản dịch", () => Copy(translated));
                Add(actions, "Dịch → Note", () => { if (Current) _page?.SendSelectionToNote(translated); Hide(); });
                _translation.Children.Add(actions);
            }
            foreach (var definition in result.Definitions) Text(_translation, definition);
            if (result.AudioUrl is { } audio)
                Add(_translation, "Nghe phát âm", () =>
                {
                    _audio?.Dispose();
                    _audio = new MediaPlayer { Source = MediaSource.CreateFromUri(new Uri(audio)) };
                    _audio.Play();
                });
            Text(_translation, "Nghĩa: Wiktionary (CC BY-SA) · Gợi ý dịch: MyMemory");
        }
        catch (OperationCanceledException)
        {
            if (!cancellation.IsCancellationRequested && Current) status.Text = "Dịch vụ phản hồi chậm. Bấm Dịch để thử lại.";
        }
        catch (Exception exception)
        {
            if (!cancellation.IsCancellationRequested && Current) status.Text = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_lookup, cancellation)) _lookup = null;
        }
    }

    private static async Task OpenOxfordAsync(string text)
    {
        try { await Launcher.LaunchUriAsync(new Uri("https://www.oxfordlearnersdictionaries.com/search/english/?q=" + Uri.EscapeDataString(text))); }
        catch (Exception) { /* A missing browser does not invalidate the selection. */ }
    }

    private void Cancel()
    {
        _lookup?.Cancel();
        _lookup = null;
        _audio?.Dispose();
        _audio = null;
        _page = null;
        _selection = null;
    }

    private static void Copy(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private static TextBlock Text(StackPanel panel, string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        panel.Children.Add(block);
        return block;
    }

    private static void Add(StackPanel panel, string label, Action action)
    {
        var button = new Button { Content = label, FontSize = 11, Padding = new Thickness(7, 5, 7, 5) };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
    }
}
