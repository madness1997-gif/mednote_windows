using MedNote.Core;

namespace MedNote.Windows.App.ViewModels;

public sealed partial class ReaderViewModel
{
    public async Task InitializeAsync(bool reopenActiveDocument = true, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            await _persistence.LoadAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            StatusText = $"Không đọc được trạng thái cũ: {exception.Message}";
            return;
        }

        var active = _persistence.ActiveDocument;
        if (!reopenActiveDocument || active is null || !File.Exists(active.Path))
        {
            return;
        }

        try
        {
            await OpenDocumentAsync(active.Path, cancellationToken);
        }
        catch (PdfPasswordRequiredException)
        {
            PendingPasswordDocumentPath = active.Path;
            StatusText = "Tài liệu gần nhất cần nhập lại mật khẩu";
        }
        catch (Exception exception)
        {
            StatusText = $"Không mở lại được tài liệu gần nhất: {exception.Message}";
        }
    }

    public Task OpenDocumentAsync(string path, CancellationToken cancellationToken = default) =>
        OpenDocumentAsync(path, password: null, cancellationToken: cancellationToken);

    public async Task OpenDocumentAsync(
        string path,
        string? password,
        CancellationToken cancellationToken = default)
    {
        await _documentGate.WaitAsync(cancellationToken);
        var previousStatus = StatusText;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            IsBusy = true;
            StatusText = "Đang mở PDF…";
            await ReplaceDocumentSessionAsync(path, password, cancellationToken);
            PendingPasswordDocumentPath = null;
        }
        catch
        {
            StatusText = previousStatus;
            throw;
        }
        finally
        {
            IsBusy = false;
            _documentGate.Release();
        }
    }

    public async ValueTask PersistNowAsync(CancellationToken cancellationToken = default)
    {
        var document = SnapshotDocument();
        if (document is not null)
        {
            await _persistence.SaveNowAsync(document, cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _documentGate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await _search.CancelAsync(clearResults: true);
            ClearTextSelection();
            try
            {
                if (HasDocument)
                {
                    await PersistNowAsync();
                }
            }
            catch
            {
                // State is also persisted after every interaction; shutdown stays unblocked.
            }

            try
            {
                foreach (var page in Pages)
                {
                    page.Dispose();
                }

                _bitmapBudget.Clear();
                if (_session is not null)
                {
                    await _session.DisposeAsync();
                    _session = null;
                }
            }
            finally
            {
                try
                {
                    await _persistence.DisposeAsync();
                }
                finally
                {
                    if (_pdfEngine is IAsyncDisposable disposableEngine)
                    {
                        await disposableEngine.DisposeAsync();
                    }
                }
            }

            _search.PropertyChanged -= OnSearchCoordinatorPropertyChanged;
            await _search.DisposeAsync();
        }
        finally
        {
            _documentGate.Release();
        }
    }

    private async Task ReplaceDocumentSessionAsync(
        string path,
        string? password,
        CancellationToken cancellationToken)
    {
        IPdfDocumentSession? nextSession = null;
        try
        {
            var fullPath = System.IO.Path.GetFullPath(path);
            var info = new FileInfo(fullPath);
            if (!info.Exists)
            {
                throw new FileNotFoundException("Không tìm thấy tệp PDF.", fullPath);
            }

            await _search.CancelAsync(clearResults: true, cancellationToken);
            ClearTextSelection();

            var lastModified = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds();
            var documentId = DocumentIdentity.Create(info.Name, info.Length, lastModified);
            nextSession = await _pdfEngine.OpenAsync(fullPath, password, cancellationToken);
            var existing = _persistence.FindDocument(documentId);
            var nextReader = (existing?.Reader ?? new ReaderState()).Normalize(nextSession.PageCount);
            var nextPosition = (existing?.Position ?? new ReaderPosition { AnchorPage = nextReader.Page }).Normalize(nextSession.PageCount);

            var oldSession = _session;
            var oldPages = Pages;
            var openedSession = nextSession;
            _session = openedSession;
            nextSession = null;
            _documentId = documentId;
            _documentPath = fullPath;
            _documentSize = info.Length;
            _documentLastModified = lastModified;
            _reader = nextReader;
            _position = nextPosition;
            DocumentName = info.Name;
            PageCount = openedSession.PageCount;
            CurrentPage = nextReader.Page;
            Zoom = nextReader.Zoom;
            Rotation = nextReader.Rotation;
            FitMode = nextReader.FitMode;
            ViewMode = nextReader.ViewMode;
            Bookmarks = nextReader.Bookmarks.ToArray();
            Pages = Enumerable.Range(0, PageCount)
                .Select(index =>
                {
                    var metrics = openedSession.PageMetrics[index];
                    var layout = CalculatePageLayout(metrics.AspectRatioForRotation(Rotation));
                    return new PdfPageViewModel(
                        this,
                        openedSession,
                        _bitmapBudget,
                        _renderScheduler,
                        documentId,
                        index,
                        metrics,
                        Rotation,
                        layout.Width,
                        layout.Height);
                })
                .ToArray();
            HasDocument = true;
            StatusText = $"{PageCount:N0} trang";
            await _search.ConfigureAsync(openedSession as IPdfTextProvider, PageCount);

            foreach (var page in oldPages)
            {
                page.Dispose();
            }

            if (oldSession is not null)
            {
                await oldSession.DisposeAsync();
            }

            QueuePersist();
        }
        finally
        {
            if (nextSession is not null)
            {
                await nextSession.DisposeAsync();
            }
        }
    }

    private void QueuePersist()
    {
        if (!HasDocument)
        {
            return;
        }

        var document = SnapshotDocument();
        if (document is not null)
        {
            _persistence.QueueSave(
                document,
                exception => StatusText = $"Chưa lưu được vị trí đọc: {exception.Message}");
        }
    }

    private ReaderDocumentRecord? SnapshotDocument()
    {
        if (_documentId is null || _documentPath is null)
        {
            return null;
        }

        return new ReaderDocumentRecord
        {
            Id = _documentId,
            Name = DocumentName,
            Path = _documentPath,
            Size = _documentSize,
            LastModified = _documentLastModified,
            Reader = _reader.Normalize(PageCount),
            Position = _position.Normalize(PageCount),
        };
    }
}
