using MedNote.Core;

namespace MedNote.Infrastructure;

/// <summary>
/// Presents the native Document graph through the existing Reader persistence
/// contract so Reader and Note no longer maintain two live libraries.
/// </summary>
public sealed class NativeReaderLibraryStore(INoteRepository repository) : IReaderLibraryStore
{
    private readonly INoteRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async ValueTask<ReaderLibrary> LoadAsync(CancellationToken cancellationToken = default)
    {
        var graph = await _repository.LoadDocumentGraphAsync(cancellationToken);
        if (graph is null)
        {
            return new ReaderLibrary();
        }

        var runtime = await _repository.LoadRuntimeMetadataAsync(cancellationToken);
        return ReaderDocumentGraphBridge.Read(graph, runtime?.SavedAt ?? 0);
    }

    public async ValueTask SaveAsync(ReaderLibrary library, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        _ = await _repository.MergeReaderLibraryAsync(library, cancellationToken);
    }
}
