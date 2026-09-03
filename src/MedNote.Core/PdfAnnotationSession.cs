using System.Text.Json;

namespace MedNote.Core;

/// <summary>
/// Per-document annotation edit session. History is intentionally transient,
/// while every committed snapshot is immediately available for Reader JSON
/// persistence. Unknown future-web annotation records remain byte-semantic
/// JSON values across native edits and undo/redo.
/// </summary>
public sealed class PdfAnnotationSession
{
    public const int DefaultHistoryLimit = 60;

    private readonly int _historyLimit;
    private readonly List<List<JsonElement>> _undo = [];
    private readonly List<List<JsonElement>> _redo = [];
    private List<JsonElement> _current = [];
    private IReadOnlyList<PdfAnnotation> _known = Array.Empty<PdfAnnotation>();

    public PdfAnnotationSession(int historyLimit = DefaultHistoryLimit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(historyLimit, 1);
        _historyLimit = historyLimit;
    }

    public IReadOnlyList<PdfAnnotation> Annotations => _known;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void Reset(IEnumerable<JsonElement>? annotations)
    {
        _current = Clone(annotations ?? []);
        _undo.Clear();
        _redo.Clear();
        RefreshKnown();
    }

    public bool Add(PdfAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        var next = Clone(_current);
        next.Add(PdfAnnotationJson.Serialize(annotation));
        return Commit(next);
    }

    public bool ReplacePage(int page, IEnumerable<PdfAnnotation> annotations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentNullException.ThrowIfNull(annotations);
        var next = _current
            .Where(element => !PdfAnnotationJson.TryDeserialize(element, out var annotation)
                || annotation!.Page != page)
            .Select(element => element.Clone())
            .ToList();
        next.AddRange(annotations.Select(annotation =>
            PdfAnnotationJson.Serialize(annotation with { Page = page })));
        return Commit(next);
    }

    public bool Delete(string annotationId)
    {
        if (string.IsNullOrWhiteSpace(annotationId))
        {
            return false;
        }

        var next = _current
            .Where(element => !PdfAnnotationJson.TryDeserialize(element, out var annotation)
                || !string.Equals(annotation!.Id, annotationId, StringComparison.Ordinal))
            .Select(element => element.Clone())
            .ToList();
        return Commit(next);
    }

    public bool Delete(IEnumerable<string> annotationIds)
    {
        ArgumentNullException.ThrowIfNull(annotationIds);
        var ids = annotationIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0)
        {
            return false;
        }

        var next = _current
            .Where(element => !PdfAnnotationJson.TryDeserialize(element, out var annotation)
                || !ids.Contains(annotation!.Id))
            .Select(element => element.Clone())
            .ToList();
        return Commit(next);
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        PushBounded(_redo, _current);
        _current = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        RefreshKnown();
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        PushBounded(_undo, _current);
        _current = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        RefreshKnown();
        return true;
    }

    public List<JsonElement> SnapshotJson() => Clone(_current);

    private bool Commit(List<JsonElement> next)
    {
        if (JsonEquals(_current, next))
        {
            return false;
        }

        PushBounded(_undo, _current);
        _current = next;
        _redo.Clear();
        RefreshKnown();
        return true;
    }

    private void PushBounded(List<List<JsonElement>> history, IEnumerable<JsonElement> snapshot)
    {
        history.Add(Clone(snapshot));
        if (history.Count > _historyLimit)
        {
            history.RemoveRange(0, history.Count - _historyLimit);
        }
    }

    private void RefreshKnown() => _known = PdfAnnotationJson.DeserializeKnown(_current);

    private static List<JsonElement> Clone(IEnumerable<JsonElement> source) =>
        source.Select(element => element.Clone()).ToList();

    private static bool JsonEquals(IReadOnlyList<JsonElement> left, IReadOnlyList<JsonElement> right) =>
        left.Count == right.Count
        && left.Select(element => element.GetRawText())
            .SequenceEqual(right.Select(element => element.GetRawText()), StringComparer.Ordinal);
}
