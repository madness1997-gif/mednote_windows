namespace MedNote.Core;

/// <summary>
/// Serializes and debounces local Reader state independently of the UI/session
/// view-model. Unknown per-document fields survive every upsert.
/// </summary>
public sealed class ReaderPersistenceCoordinator : IAsyncDisposable
{
    private readonly IReaderLibraryStore _store;
    private readonly object _sync = new();
    private CancellationTokenSource? _saveDelayCancellation;
    private ReaderLibrary _library = new();
    private bool _disposed;

    public ReaderPersistenceCoordinator(IReaderLibraryStore store)
    {
        _store = store;
    }

    public ReaderDocumentRecord? ActiveDocument =>
        _library.Documents.FirstOrDefault(document => document.Id == _library.ActiveDocumentId);

    public ReaderDocumentRecord? FindDocument(string id) =>
        _library.Documents.FirstOrDefault(document => document.Id == id);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _library = await _store.LoadAsync(cancellationToken);
    }

    public void QueueSave(ReaderDocumentRecord document, Action<Exception>? onFailure = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReaderLibrary snapshot;
        CancellationToken token;
        lock (_sync)
        {
            _saveDelayCancellation?.Cancel();
            _saveDelayCancellation?.Dispose();
            _saveDelayCancellation = new CancellationTokenSource();
            token = _saveDelayCancellation.Token;
            snapshot = Upsert(document);
        }

        _ = SaveAfterDelayAsync(snapshot, token, onFailure);
    }

    public async ValueTask SaveNowAsync(ReaderDocumentRecord document, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReaderLibrary snapshot;
        lock (_sync)
        {
            _saveDelayCancellation?.Cancel();
            _saveDelayCancellation?.Dispose();
            _saveDelayCancellation = null;
            snapshot = Upsert(document);
        }

        await _store.SaveAsync(snapshot, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _saveDelayCancellation?.Cancel();
        _saveDelayCancellation?.Dispose();
        if (_store is IDisposable disposableStore)
        {
            disposableStore.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private ReaderLibrary Upsert(ReaderDocumentRecord document)
    {
        var existing = FindDocument(document.Id);
        var preserved = existing is null || document.ExtensionData.Count > 0
            ? document
            : document with { ExtensionData = existing.ExtensionData };
        _library = _library.Upsert(preserved);
        return _library;
    }

    private async Task SaveAfterDelayAsync(
        ReaderLibrary snapshot,
        CancellationToken cancellationToken,
        Action<Exception>? onFailure)
    {
        try
        {
            await Task.Delay(350, cancellationToken);
            await _store.SaveAsync(snapshot, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A newer state superseded this snapshot.
        }
        catch (Exception exception)
        {
            onFailure?.Invoke(exception);
        }
    }
}
