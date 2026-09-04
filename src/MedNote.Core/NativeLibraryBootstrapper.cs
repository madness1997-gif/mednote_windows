namespace MedNote.Core;

public sealed record NativeLibraryBootstrapResult(
    bool MigratedReaderV1,
    bool CreatedDefaultNote,
    NoteStructure Notes,
    LibraryPreferences Preferences);

/// <summary>
/// Performs the one-time Reader-v1 cut-over without deleting the legacy file,
/// then guarantees that the native Note hierarchy has one editable Sheet.
/// </summary>
public sealed class NativeLibraryBootstrapper(INoteRepository repository)
{
    private readonly INoteRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async ValueTask<NativeLibraryBootstrapResult> InitializeAsync(
        IReaderLibraryStore legacyReaderStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(legacyReaderStore);
        var notes = await _repository.LoadNoteStructureAsync(cancellationToken);
        var migrated = false;
        if (notes is null)
        {
            var readerV1 = await legacyReaderStore.LoadAsync(cancellationToken);
            await _repository.ReplaceLibraryAsync(ReaderV1Migration.CreateLibrary(readerV1), cancellationToken);
            notes = await _repository.LoadNoteStructureAsync(cancellationToken)
                ?? throw new InvalidDataException("Không tải lại được Note Library sau migration Reader v1.");
            migrated = true;
        }

        var createdDefault = false;
        if (notes.Sheets.Count == 0)
        {
            await _repository.CreateNotebookAsync(
                new CreateNotebookRequest
                {
                    Title = "Sổ ghi chú",
                    SectionTitle = "Ghi chú",
                    PageTitle = "Trang mới",
                },
                NativeNoteTemplates.FirstAid(),
                cancellationToken);
            notes = await _repository.LoadNoteStructureAsync(cancellationToken)
                ?? throw new InvalidDataException("Không tải lại được cấu trúc Note mặc định.");
            createdDefault = true;
        }

        var runtime = await _repository.LoadRuntimeMetadataAsync(cancellationToken)
            ?? throw new InvalidDataException("Note Library thiếu runtime metadata.");
        return new NativeLibraryBootstrapResult(migrated, createdDefault, notes, runtime.Preferences);
    }
}
