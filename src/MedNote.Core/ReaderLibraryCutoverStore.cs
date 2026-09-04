namespace MedNote.Core;

/// <summary>
/// Keeps Reader v1 live until native bootstrap has completed and been verified.
/// Activation is explicit and one-way; ownership of both stores remains with
/// the composition root.
/// </summary>
public sealed class ReaderLibraryCutoverStore : IReaderLibraryStore
{
    private readonly IReaderLibraryStore _nativeStore;
    private IReaderLibraryStore _activeStore;
    private int _nativeActive;

    public ReaderLibraryCutoverStore(IReaderLibraryStore legacyStore, IReaderLibraryStore nativeStore)
    {
        _activeStore = legacyStore ?? throw new ArgumentNullException(nameof(legacyStore));
        _nativeStore = nativeStore ?? throw new ArgumentNullException(nameof(nativeStore));
    }

    public bool NativeActive => Volatile.Read(ref _nativeActive) != 0;

    public void ActivateNative()
    {
        Volatile.Write(ref _activeStore, _nativeStore);
        Volatile.Write(ref _nativeActive, 1);
    }

    public ValueTask<ReaderLibrary> LoadAsync(CancellationToken cancellationToken = default) =>
        Volatile.Read(ref _activeStore).LoadAsync(cancellationToken);

    public ValueTask SaveAsync(ReaderLibrary library, CancellationToken cancellationToken = default) =>
        Volatile.Read(ref _activeStore).SaveAsync(library, cancellationToken);
}
