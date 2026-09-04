using MedNote.Core;
using MedNote.Windows.App.ViewModels;

namespace MedNote.Windows.App;

public sealed partial class MainWindow
{
    private readonly object _noteIntegrationSync = new();
    private Task _noteIntegrationTask = Task.CompletedTask;
    private CancellationTokenSource? _sourceFocusCancellation;
    private PdfPageViewModel? _sourceFocusPage;

    private void OnReaderCropCreated(PdfCropResult crop) =>
        QueueNoteIntegration(() => HandleReaderCropCreatedAsync(crop));

    private async Task HandleReaderCropCreatedAsync(PdfCropResult crop)
    {
        if (_closing)
        {
            return;
        }

        try
        {
            if (_closing)
            {
                return;
            }

            if (!NoteViewModel.IsReady || ViewModel.DocumentId is not { } documentId)
            {
                throw new InvalidOperationException("Note chưa sẵn sàng nhận crop PDF.");
            }

            await NoteWorkspacePane.InsertPdfCropAsync(
                crop,
                documentId,
                ViewModel.DocumentName,
                cancellationToken => ViewModel.PersistNowAsync(cancellationToken));
            if (_workspace?.Mode == WorkspaceMode.Reader)
            {
                await ChangeWorkspaceModeAsync(WorkspaceMode.Split, focusTarget: false);
            }

            ViewModel.ReportCropInserted(crop.Page);
            NoteWorkspacePane.FocusEditor();
        }
        catch (Exception exception)
        {
            if (!_closing)
            {
                await ShowErrorAsync("Không chèn được crop vào Note", exception.Message);
            }
        }
    }

    private void OnNoteSourceRequested(NoteSourceAnchorItem source) =>
        QueueNoteIntegration(() => HandleNoteSourceRequestedAsync(source));

    private async Task HandleNoteSourceRequestedAsync(NoteSourceAnchorItem source)
    {
        if (_closing)
        {
            return;
        }

        try
        {
            if (_closing)
            {
                return;
            }

            if (ViewModel.DocumentId != source.DocumentId)
            {
                if (string.IsNullOrWhiteSpace(source.LocalPath) || !File.Exists(source.LocalPath))
                {
                    throw new FileNotFoundException(
                        $"Không còn tìm thấy tệp nguồn {source.DocumentName}.",
                        source.LocalPath);
                }

                var sourceFile = new FileInfo(source.LocalPath);
                var lastModified = new DateTimeOffset(sourceFile.LastWriteTimeUtc).ToUnixTimeMilliseconds();
                var currentIdentity = DocumentIdentity.Create(sourceFile.Name, sourceFile.Length, lastModified);
                if (currentIdentity != source.DocumentId)
                {
                    throw new InvalidDataException(
                        $"Tệp {source.DocumentName} đã thay đổi sau khi tạo nguồn; anchor cũ được giữ nguyên.");
                }

                await OpenFileAsync(source.LocalPath);
                if (ViewModel.DocumentId != source.DocumentId)
                {
                    return;
                }
            }

            if (_workspace?.Mode == WorkspaceMode.Note)
            {
                await ChangeWorkspaceModeAsync(WorkspaceMode.Split, focusTarget: false);
            }

            ShowSourceFocus(source.Page, source.Rect);
            await _viewport.NavigateToSourceAsync(source.Page, source.Rect);
            _state?.UpdatePageControls();
            NoteViewModel.ReportSourceOpened(source);
            FocusReaderPane();
        }
        catch (Exception exception)
        {
            if (!_closing)
            {
                await ShowErrorAsync("Không mở được nguồn PDF", exception.Message);
            }
        }
    }

    private void QueueNoteIntegration(Func<Task> operation)
    {
        lock (_noteIntegrationSync)
        {
            if (_closing)
            {
                return;
            }

            _noteIntegrationTask = ContinueNoteIntegrationAsync(_noteIntegrationTask, operation);
        }
    }

    private static async Task ContinueNoteIntegrationAsync(Task previous, Func<Task> operation)
    {
        try
        {
            await previous;
        }
        catch
        {
            // Each queued operation handles its own UI error. Keep the queue usable
            // if an unexpected exception escapes one operation.
        }

        await operation();
    }

    private Task BeginNoteIntegrationShutdown()
    {
        lock (_noteIntegrationSync)
        {
            _closing = true;
            return _noteIntegrationTask;
        }
    }

    private void ShowSourceFocus(int page, PdfAnnotationRect? rectangle)
    {
        _sourceFocusCancellation?.Cancel();
        _sourceFocusCancellation?.Dispose();
        _sourceFocusPage?.SetSourceFocus(null);
        _sourceFocusPage = null;
        if (rectangle is null || page < 1 || page > ViewModel.Pages.Count)
        {
            return;
        }

        var focusedPage = ViewModel.Pages[page - 1];
        focusedPage.SetSourceFocus(rectangle);
        _sourceFocusPage = focusedPage;
        _sourceFocusCancellation = new CancellationTokenSource();
        _ = ClearSourceFocusAsync(focusedPage, _sourceFocusCancellation.Token);
    }

    private async Task ClearSourceFocusAsync(PdfPageViewModel page, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3.6d), cancellationToken);
            page.SetSourceFocus(null);
            if (ReferenceEquals(_sourceFocusPage, page))
            {
                _sourceFocusPage = null;
            }
        }
        catch (OperationCanceledException)
        {
            // A newer source navigation superseded this transient focus.
        }
    }
}
