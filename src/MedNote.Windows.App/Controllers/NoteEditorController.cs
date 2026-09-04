using MedNote.Core;
using MedNote.Windows.App.ViewModels;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MedNote.Windows.App.Controllers;

/// <summary>
/// Owns the RichEdit document boundary, formatting commands, debounced saves
/// and per-Sheet caret restoration. The repository only ever sees RTF.
/// </summary>
public sealed class NoteEditorController : IAsyncDisposable
{
    private readonly NoteViewModel _viewModel;
    private readonly RichEditBox _editor;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(550) };
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly Dictionary<string, (int Start, int End)> _selections = new(StringComparer.Ordinal);
    private bool _loading;
    private bool _disposed;

    public NoteEditorController(NoteViewModel viewModel, RichEditBox editor)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _saveTimer.Tick += OnSaveTimerTick;
        _editor.TextChanged += OnTextChanged;
    }

    public event Action<Exception>? SaveFailed;

    public bool ContainsFocus()
    {
        var focused = FocusManager.GetFocusedElement(_editor.XamlRoot) as DependencyObject;
        while (focused is not null)
        {
            if (ReferenceEquals(focused, _editor))
            {
                return true;
            }

            focused = VisualTreeHelper.GetParent(focused);
        }

        return false;
    }

    public void FocusEditor() => _editor.Focus(FocusState.Programmatic);

    public void LoadActiveSheet()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _saveTimer.Stop();
        _loading = true;
        try
        {
            _editor.Document.SetText(TextSetOptions.FormatRtf, _viewModel.ActiveRtf);
            if (_selections.TryGetValue(_viewModel.Active.ActiveSheetId, out var selection))
            {
                _editor.Document.Selection.SetRange(selection.Start, selection.End);
            }
            else
            {
                _editor.Document.Selection.SetRange(0, 0);
            }
        }
        finally
        {
            _loading = false;
        }
    }

    public void CaptureSelection()
    {
        if (!_viewModel.IsReady || string.IsNullOrEmpty(_viewModel.Active.ActiveSheetId))
        {
            return;
        }

        var selection = _editor.Document.Selection;
        _selections[_viewModel.Active.ActiveSheetId] = (selection.StartPosition, selection.EndPosition);
    }

    public async Task SaveNowAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _saveTimer.Stop();
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            _editor.Document.GetText(TextGetOptions.FormatRtf, out var rtf);
            await _viewModel.SaveActiveContentAsync(rtf, cancellationToken);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public void ToggleBold() => ToggleCharacterFormat(format => format.Bold = FormatEffect.Toggle);

    public void ToggleItalic() => ToggleCharacterFormat(format => format.Italic = FormatEffect.Toggle);

    public void ToggleUnderline() => ToggleCharacterFormat(format =>
        format.Underline = format.Underline == UnderlineType.Single ? UnderlineType.None : UnderlineType.Single);

    public void SetFontSize(float points) => ToggleCharacterFormat(format => format.Size = Math.Clamp(points, 8f, 48f));

    public void ToggleBulletList() => ToggleList(MarkerType.Bullet);

    public void ToggleNumberedList() => ToggleList(MarkerType.Arabic);

    public void InsertFirstAid()
    {
        _editor.Document.Selection.SetText(TextSetOptions.FormatRtf, NativeNoteTemplates.FirstAidRtf);
        QueueSave();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _saveTimer.Stop();
        try
        {
            await SaveNowAsync();
        }
        catch (Exception exception)
        {
            SaveFailed?.Invoke(exception);
        }
        finally
        {
            _disposed = true;
            _saveTimer.Tick -= OnSaveTimerTick;
            _editor.TextChanged -= OnTextChanged;
            _saveGate.Dispose();
        }
    }

    private void OnTextChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading && _viewModel.IsReady)
        {
            QueueSave();
        }
    }

    private void QueueSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private async void OnSaveTimerTick(object? sender, object e)
    {
        _saveTimer.Stop();
        try
        {
            await SaveNowAsync();
        }
        catch (Exception exception)
        {
            SaveFailed?.Invoke(exception);
        }
    }

    private void ToggleCharacterFormat(Action<ITextCharacterFormat> update)
    {
        var format = _editor.Document.Selection.CharacterFormat;
        update(format);
        _editor.Document.Selection.CharacterFormat = format;
        QueueSave();
        FocusEditor();
    }

    private void ToggleList(MarkerType marker)
    {
        var format = _editor.Document.Selection.ParagraphFormat;
        format.ListType = format.ListType == marker ? MarkerType.None : marker;
        _editor.Document.Selection.ParagraphFormat = format;
        QueueSave();
        FocusEditor();
    }
}
