using MedNote.Core;

namespace MedNote.Windows.App.ViewModels;

public sealed partial class ReaderViewModel
{
    public IReadOnlyList<PdfAnnotation> Annotations => _annotationSession.Annotations;

    public int AnnotationCount => Annotations.Count;

    public bool CanUndoAnnotations => _annotationSession.CanUndo;

    public bool CanRedoAnnotations => _annotationSession.CanRedo;

    public string InkColor
    {
        get => _inkColor;
        private set => SetProperty(ref _inkColor, value);
    }

    public string HighlightColor
    {
        get => _highlightColor;
        private set => SetProperty(ref _highlightColor, value);
    }

    public double InkWidth
    {
        get => _inkWidth;
        private set => SetProperty(ref _inkWidth, value);
    }

    public PdfCropResult? LastCropResult
    {
        get => _lastCropResult;
        private set => SetProperty(ref _lastCropResult, value);
    }

    public IReadOnlyList<PdfAnnotation> GetAnnotationsForPage(int page) =>
        Annotations.Where(annotation => annotation.Page == page).ToArray();

    public void SetInkColor(string color) => InkColor = PdfAnnotationColor.Normalize(color);

    public void SetHighlightColor(string color) => HighlightColor = PdfAnnotationColor.Normalize(color);

    public void SetInkWidth(double width) =>
        InkWidth = Math.Clamp(double.IsFinite(width) ? width : 2d, 1d, 16d);

    public bool AddAnnotation(PdfAnnotation annotation)
    {
        if (!HasDocument)
        {
            return false;
        }

        annotation = annotation.Normalize(PageCount);
        var before = Annotations.ToArray();
        return ApplyAnnotationMutation(before, _annotationSession.Add(annotation));
    }

    public bool DeleteAnnotations(IEnumerable<string> annotationIds)
    {
        var before = Annotations.ToArray();
        return ApplyAnnotationMutation(before, _annotationSession.Delete(annotationIds));
    }

    public bool UndoAnnotations()
    {
        var before = Annotations.ToArray();
        return ApplyAnnotationMutation(before, _annotationSession.Undo());
    }

    public bool RedoAnnotations()
    {
        var before = Annotations.ToArray();
        return ApplyAnnotationMutation(before, _annotationSession.Redo());
    }

    public bool AddSelectionMarkup(PdfPageViewModel page, PdfAnnotationKind kind)
    {
        if (page.Selection is not { } selection || !IsTextMarkup(kind))
        {
            return false;
        }

        var color = kind is PdfAnnotationKind.Highlight or PdfAnnotationKind.AreaHighlight
            ? HighlightColor
            : kind is PdfAnnotationKind.Underline or PdfAnnotationKind.Squiggly
                ? InkColor
                : "#c94b50";
        var annotation = new PdfAnnotation
        {
            Id = CreateAnnotationId(kind),
            Kind = kind,
            Page = page.Number,
            Color = color,
            Rects = selection.Bounds.Select(page.PageRectToAnnotation).ToList(),
            Text = selection.Text,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        var added = AddAnnotation(annotation);
        if (added)
        {
            ClearTextSelection();
        }

        return added;
    }

    public bool CommitSelectionMarkupForActiveTool(PdfPageViewModel page)
    {
        var kind = ActiveTool switch
        {
            PdfTool.Highlight => PdfAnnotationKind.Highlight,
            PdfTool.Underline => PdfAnnotationKind.Underline,
            PdfTool.Strikeout => PdfAnnotationKind.Strikeout,
            PdfTool.Squiggly => PdfAnnotationKind.Squiggly,
            _ => (PdfAnnotationKind?)null,
        };
        return kind is not null && AddSelectionMarkup(page, kind.Value);
    }

    public async ValueTask<PdfCropResult?> CreateCropAsync(
        PdfPageViewModel page,
        PdfAnnotationRect rect,
        CancellationToken cancellationToken = default)
    {
        if (_session is not IPdfCropProvider cropProvider)
        {
            StatusText = "PDF engine chưa hỗ trợ crop";
            return null;
        }

        var result = await cropProvider.CropAsync(
            new PdfCropRequest(page.PageIndex, rect, Rotation),
            cancellationToken);
        LastCropResult = result;
        StatusText = $"Đã crop trang {result.Page} — sẵn sàng gửi sang Note";
        return result;
    }

    public async ValueTask ExportFlattenedAsync(
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (_session is not IPdfAnnotationExportProvider exporter)
        {
            throw new NotSupportedException("PDF engine chưa hỗ trợ xuất annotation phẳng.");
        }

        IsBusy = true;
        StatusText = "Đang xuất PDF có chú thích…";
        try
        {
            await exporter.ExportFlattenedAsync(outputPath, Annotations, cancellationToken);
            StatusText = $"Đã xuất {System.IO.Path.GetFileName(outputPath)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal static string CreateAnnotationId(PdfAnnotationKind kind) =>
        $"pdf-{KindToken(kind)}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}";

    private bool ApplyAnnotationMutation(IReadOnlyList<PdfAnnotation> before, bool changed)
    {
        if (!changed)
        {
            return false;
        }

        _reader = _reader with { Annotations = _annotationSession.SnapshotJson() };
        var affectedPages = before.Select(annotation => annotation.Page)
            .Concat(Annotations.Select(annotation => annotation.Page))
            .Distinct()
            .ToArray();
        foreach (var pageNumber in affectedPages)
        {
            if (pageNumber >= 1 && pageNumber <= Pages.Count)
            {
                Pages[pageNumber - 1].NotifyAnnotationsChanged();
            }
        }

        OnPropertyChanged(nameof(Annotations));
        OnPropertyChanged(nameof(AnnotationCount));
        OnPropertyChanged(nameof(CanUndoAnnotations));
        OnPropertyChanged(nameof(CanRedoAnnotations));
        QueuePersist();
        return true;
    }

    private static bool IsTextMarkup(PdfAnnotationKind kind) => kind is
        PdfAnnotationKind.Highlight
        or PdfAnnotationKind.Underline
        or PdfAnnotationKind.Strikeout
        or PdfAnnotationKind.Squiggly;

    private static string KindToken(PdfAnnotationKind kind) => kind switch
    {
        PdfAnnotationKind.AreaHighlight => "area-highlight",
        PdfAnnotationKind.Strikeout => "strikeout",
        _ => kind.ToString().ToLowerInvariant(),
    };
}
