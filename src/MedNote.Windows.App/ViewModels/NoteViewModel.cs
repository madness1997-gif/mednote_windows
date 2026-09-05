using MedNote.Core;

namespace MedNote.Windows.App.ViewModels;

public sealed record NoteSheetNavigationItem(string Id, string Label);

public sealed record NoteSourceAnchorItem(
    string RelationId,
    string DocumentId,
    string DocumentName,
    string? LocalPath,
    int Page,
    PdfAnnotationRect? Rect)
{
    public string DisplayLabel => $"{DocumentName} · tr. {Page}";
}

public sealed class NoteViewModel(INoteRepository repository) : ObservableObject
{
    private readonly INoteRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly SemaphoreSlim _preferencesGate = new(1, 1);
    private NoteStructure _notes = new();
    private IReadOnlyList<NotebookRecord> _notebooks = [];
    private IReadOnlyList<SectionRecord> _sections = [];
    private IReadOnlyList<PageRecord> _pages = [];
    private IReadOnlyList<NoteSheetNavigationItem> _sheets = [];
    private IReadOnlyList<NoteSourceAnchorItem> _sources = [];
    private LibraryPreferences _preferences = new();
    private string _activeRtf = RtfDocument.Empty;
    private string _activePageTitle = "Trang mới";
    private string _statusText = "Đang khởi tạo Note…";
    private bool _isReady;
    private bool _isBusy;

    public IReadOnlyList<NotebookRecord> Notebooks
    {
        get => _notebooks;
        private set => SetProperty(ref _notebooks, value);
    }

    public IReadOnlyList<SectionRecord> Sections
    {
        get => _sections;
        private set => SetProperty(ref _sections, value);
    }

    public IReadOnlyList<PageRecord> Pages
    {
        get => _pages;
        private set => SetProperty(ref _pages, value);
    }

    public IReadOnlyList<NoteSheetNavigationItem> Sheets
    {
        get => _sheets;
        private set => SetProperty(ref _sheets, value);
    }

    public IReadOnlyList<NoteSourceAnchorItem> Sources
    {
        get => _sources;
        private set => SetProperty(ref _sources, value);
    }

    public LibraryPreferences Preferences
    {
        get => _preferences;
        private set => SetProperty(ref _preferences, value);
    }

    public ActiveNoteState Active => _notes.Active;

    public string ActiveRtf
    {
        get => _activeRtf;
        private set => SetProperty(ref _activeRtf, value);
    }

    public string ActivePageTitle
    {
        get => _activePageTitle;
        private set => SetProperty(ref _activePageTitle, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsReady
    {
        get => _isReady;
        private set => SetProperty(ref _isReady, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public async Task InitializeAsync(
        IReaderLibraryStore legacyReaderStore,
        CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            var result = await new NativeLibraryBootstrapper(_repository).InitializeAsync(
                legacyReaderStore,
                cancellationToken);
            Preferences = result.Preferences;
            await ApplyStructureAsync(result.Notes, cancellationToken);
            IsReady = true;
            StatusText = result.MigratedReaderV1
                ? "Đã chuyển dữ liệu Reader sang Library native"
                : "Đã lưu";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SelectNotebookAsync(string notebookId, CancellationToken cancellationToken = default) =>
        await SelectSheetAsync(FirstSheetInNotebook(notebookId)?.Id, cancellationToken);

    public async Task SelectSectionAsync(string sectionId, CancellationToken cancellationToken = default) =>
        await SelectSheetAsync(FirstSheetInSection(sectionId)?.Id, cancellationToken);

    public async Task SelectPageAsync(string pageId, CancellationToken cancellationToken = default) =>
        await SelectSheetAsync(_notes.Sheets.OrderBy(sheet => sheet.Order).FirstOrDefault(sheet => sheet.PageId == pageId)?.Id, cancellationToken);

    public async Task SelectSheetAsync(string? sheetId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sheetId) || sheetId == Active.ActiveSheetId)
        {
            return;
        }

        var sheet = _notes.Sheets.FirstOrDefault(record => record.Id == sheetId);
        if (sheet is null)
        {
            return;
        }

        var page = _notes.Pages.Single(record => record.Id == sheet.PageId);
        var section = _notes.Sections.Single(record => record.Id == page.SectionId);
        await _repository.SetActiveStateAsync(
            new ActiveNoteState
            {
                ActiveNotebookId = section.NotebookId,
                ActiveSectionId = section.Id,
                ActivePageId = page.Id,
                ActiveSheetId = sheet.Id,
            },
            cancellationToken);
        await ReloadAsync(cancellationToken);
    }

    public async Task CreateNotebookAsync(CancellationToken cancellationToken = default)
    {
        await _repository.CreateNotebookAsync(
            new CreateNotebookRequest
            {
                Title = "Sổ ghi chú mới",
                SectionTitle = "Ghi chú",
                PageTitle = "Trang mới",
            },
            NativeNoteTemplates.FirstAid(),
            cancellationToken);
        await ReloadAsync(cancellationToken);
    }

    public async Task CreatePageAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Active.ActiveSectionId))
        {
            return;
        }

        await _repository.CreatePageAsync(
            new CreatePageRequest { SectionId = Active.ActiveSectionId, Title = "Trang mới" },
            NativeNoteTemplates.FirstAid(),
            cancellationToken);
        await ReloadAsync(cancellationToken);
    }

    public async Task CreateSheetAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Active.ActivePageId))
        {
            return;
        }

        await _repository.CreateSheetAsync(
            new CreateSheetRequest { PageId = Active.ActivePageId },
            RtfSheetContent.CreateEmpty(),
            cancellationToken);
        await ReloadAsync(cancellationToken);
    }

    public async Task RenameActivePageAsync(string title, CancellationToken cancellationToken = default)
    {
        title = title.Trim();
        if (string.IsNullOrEmpty(title)
            || string.IsNullOrEmpty(Active.ActivePageId)
            || string.Equals(title, ActivePageTitle, StringComparison.Ordinal))
        {
            return;
        }

        await _repository.RenamePageAsync(Active.ActivePageId, title, cancellationToken);
        await ReloadStructureAsync(cancellationToken);
        StatusText = "Đã đổi tên trang";
    }

    public async Task SaveActiveContentAsync(string rtf, CancellationToken cancellationToken = default)
    {
        if (!IsReady || string.IsNullOrEmpty(Active.ActiveSheetId) || string.Equals(rtf, ActiveRtf, StringComparison.Ordinal))
        {
            return;
        }

        var content = new RtfSheetContent { Rtf = rtf };
        NoteLibraryValidator.AssertSheetContentValid(Active.ActiveSheetId, content);
        StatusText = "Đang lưu…";
        await _repository.SaveSheetContentAsync(Active.ActiveSheetId, content, cancellationToken);
        ActiveRtf = rtf;
        StatusText = "Đã lưu";
    }

    public Task SavePdfCropAsync(
        string rtf,
        string documentId,
        PdfCropResult crop,
        CancellationToken cancellationToken = default)
        => SavePdfSourceAsync(rtf, documentId, crop.Page, crop.Rect, cancellationToken);

    public async Task SavePdfSourceAsync(string rtf, string documentId, int page,
        PdfAnnotationRect rect, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        if (!IsReady || string.IsNullOrEmpty(Active.ActiveSheetId))
        {
            throw new InvalidOperationException("Note chưa sẵn sàng nhận nội dung PDF.");
        }

        var content = new RtfSheetContent { Rtf = rtf };
        NoteLibraryValidator.AssertSheetContentValid(Active.ActiveSheetId, content);
        var pair = PdfContentLinks.Create(documentId, Active.ActiveSheetId, page, rect);
        StatusText = "Đang lưu nội dung và nguồn PDF…";
        try
        {
            var documents = await _repository.SaveLinkedSheetContentAsync(
                Active.ActiveSheetId,
                content,
                pair.Link,
                pair.Relation,
                cancellationToken);
            ActiveRtf = rtf;
            ApplySources(documents);
            StatusText = $"Đã chèn nội dung từ trang {page}";
        }
        catch
        {
            StatusText = "Chưa lưu được nội dung PDF";
            throw;
        }
    }

    public async Task SaveWorkspacePreferencesAsync(
        WorkspaceMode mode,
        double readerShare,
        CancellationToken cancellationToken = default)
    {
        await _preferencesGate.WaitAsync(cancellationToken);
        try
        {
            var next = Preferences with
            {
                WorkspaceMode = mode,
                ReaderShare = Math.Clamp(readerShare, 20d, 80d),
            };
            if (next == Preferences)
            {
                return;
            }

            await _repository.SetPreferencesAsync(next, cancellationToken);
            Preferences = next;
        }
        finally
        {
            _preferencesGate.Release();
        }
    }

    internal void ReportSourceOpened(NoteSourceAnchorItem source) =>
        StatusText = $"Đã mở {source.DocumentName} · trang {source.Page}";

    public async Task ReloadFromRepositoryAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            var metadata = await _repository.LoadRuntimeMetadataAsync(cancellationToken)
                ?? throw new InvalidDataException("Note Library native chưa được khởi tạo.");
            Preferences = metadata.Preferences;
            await ReloadAsync(cancellationToken);
            StatusText = "Đã nạp bản lưu Google Drive";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await ReloadStructureAsync(cancellationToken);
        await LoadActiveContentAsync(cancellationToken);
        await ReloadSourcesAsync(cancellationToken);
    }

    private async Task ReloadStructureAsync(CancellationToken cancellationToken)
    {
        var notes = await _repository.LoadNoteStructureAsync(cancellationToken)
            ?? throw new InvalidDataException("Note Library native chưa được khởi tạo.");
        ApplyStructure(notes);
    }

    private async Task ApplyStructureAsync(NoteStructure notes, CancellationToken cancellationToken)
    {
        ApplyStructure(notes);
        await LoadActiveContentAsync(cancellationToken);
        await ReloadSourcesAsync(cancellationToken);
    }

    private void ApplyStructure(NoteStructure notes)
    {
        _notes = notes;
        Notebooks = notes.Notebooks.OrderBy(record => record.Order).ToList();
        Sections = notes.Sections
            .Where(record => record.NotebookId == notes.Active.ActiveNotebookId)
            .OrderBy(record => record.Order)
            .ToList();
        Pages = notes.Pages
            .Where(record => record.SectionId == notes.Active.ActiveSectionId)
            .OrderBy(record => record.Order)
            .ToList();
        Sheets = notes.Sheets
            .Where(record => record.PageId == notes.Active.ActivePageId)
            .OrderBy(record => record.Order)
            .Select((record, index) => new NoteSheetNavigationItem(record.Id, $"Tờ {index + 1}"))
            .ToList();
        ActivePageTitle = notes.Pages.FirstOrDefault(record => record.Id == notes.Active.ActivePageId)?.Title ?? "Trang mới";
        OnPropertyChanged(nameof(Active));
    }

    private async Task LoadActiveContentAsync(CancellationToken cancellationToken)
    {
        var content = await _repository.LoadSheetContentAsync(Active.ActiveSheetId, cancellationToken)
            ?? throw new InvalidDataException($"Không tải được nội dung Sheet {Active.ActiveSheetId}.");
        ActiveRtf = content.Rtf;
    }

    private async Task ReloadSourcesAsync(CancellationToken cancellationToken)
    {
        var documents = await _repository.LoadDocumentGraphAsync(cancellationToken)
            ?? throw new InvalidDataException("Note Library native thiếu Document graph.");
        ApplySources(documents);
    }

    private void ApplySources(DocumentGraph graph)
    {
        Sources = PdfContentLinks.ResolveForSheet(graph, Active.ActiveSheetId)
            .Select(source => new NoteSourceAnchorItem(
                source.RelationId,
                source.DocumentId,
                source.DocumentName,
                source.LocalPath,
                source.Page,
                source.Rect))
            .ToList();
    }

    private SheetRecord? FirstSheetInNotebook(string notebookId)
    {
        foreach (var section in _notes.Sections.Where(section => section.NotebookId == notebookId).OrderBy(section => section.Order))
        {
            var sheet = FirstSheetInSection(section.Id);
            if (sheet is not null)
            {
                return sheet;
            }
        }

        return null;
    }

    private SheetRecord? FirstSheetInSection(string sectionId)
    {
        foreach (var page in _notes.Pages.Where(page => page.SectionId == sectionId).OrderBy(page => page.Order))
        {
            var sheet = _notes.Sheets.Where(sheet => sheet.PageId == page.Id).OrderBy(sheet => sheet.Order).FirstOrDefault();
            if (sheet is not null)
            {
                return sheet;
            }
        }

        return null;
    }
}
