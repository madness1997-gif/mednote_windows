using System.Text.Json;

namespace MedNote.Core;

public sealed record PdfContentLinkPair(
    NoteDocumentLink Link,
    DocumentLinkRelation Relation);

public sealed record PdfContentSource(
    string RelationId,
    string DocumentId,
    string DocumentName,
    string? LocalPath,
    int Page,
    PdfAnnotationRect? Rect,
    long CreatedAt);

public static class PdfContentLinks
{
    private static readonly JsonSerializerOptions Json = JsonDefaults.Create();

    public static PdfContentLinkPair Create(
        string documentId,
        string sheetId,
        int page,
        PdfAnnotationRect rect,
        long? createdAt = null,
        string? linkId = null,
        string? relationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetId);
        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }

        rect = rect.Normalize();
        if (rect.Width <= 0d || rect.Height <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(rect), "Vùng nguồn PDF phải có diện tích dương.");
        }

        var now = createdAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var resolvedLinkId = string.IsNullOrWhiteSpace(linkId) ? $"pdf-source-{Guid.NewGuid():N}" : linkId;
        var link = new NoteDocumentLink
        {
            Id = resolvedLinkId,
            DocumentId = documentId,
            TargetType = DocumentLinkTargetType.Sheet,
            TargetId = sheetId,
        };
        var relation = new DocumentLinkRelation
        {
            Id = string.IsNullOrWhiteSpace(relationId) ? $"pdf-source-relation-{Guid.NewGuid():N}" : relationId,
            LinkIds = [resolvedLinkId],
            Kind = DocumentRelationKind.Content,
            SourceType = DocumentRelationSourceType.Document,
            SourceId = documentId,
            CreatedAt = now,
            UpdatedAt = now,
            ContentAnchor = new DocumentContentAnchor
            {
                DocumentId = documentId,
                PdfPage = page,
                Rect = JsonSerializer.SerializeToElement(rect, Json).Clone(),
            },
        };
        return new PdfContentLinkPair(link, relation);
    }

    public static bool TryReadRect(DocumentContentAnchor? anchor, out PdfAnnotationRect rect)
    {
        rect = default;
        if (anchor?.Rect is not JsonElement value || value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryNumber(value, "x1", out var x1)
            && TryNumber(value, "y1", out var y1)
            && TryNumber(value, "x2", out var x2)
            && TryNumber(value, "y2", out var y2))
        {
            rect = new PdfAnnotationRect(x1, y1, x2, y2).Normalize();
            return rect.Width > 0d && rect.Height > 0d;
        }

        if (TryNumber(value, "x", out var x)
            && TryNumber(value, "y", out var y)
            && TryNumber(value, "width", out var width)
            && TryNumber(value, "height", out var height))
        {
            rect = new PdfAnnotationRect(x, y, x + width, y + height).Normalize();
            return rect.Width > 0d && rect.Height > 0d;
        }

        return false;
    }

    public static IReadOnlyList<PdfContentSource> ResolveForSheet(DocumentGraph graph, string sheetId)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetId);
        if (graph.Documents is null || graph.Links is null || graph.LinkRelations is null)
        {
            return [];
        }

        var documents = graph.Documents
            .Where(document => document is not null && !string.IsNullOrWhiteSpace(document.Id))
            .GroupBy(document => document.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var links = graph.Links
            .Where(link => link is not null && !string.IsNullOrWhiteSpace(link.Id))
            .GroupBy(link => link.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var sources = new List<PdfContentSource>();
        foreach (var relation in graph.LinkRelations)
        {
            if (relation is null
                || relation.Kind != DocumentRelationKind.Content
                || relation.SourceType != DocumentRelationSourceType.Document
                || relation.LinkIds is null
                || relation.ContentAnchor is not { PdfPage: >= 1 } anchor)
            {
                continue;
            }

            foreach (var linkId in relation.LinkIds)
            {
                if (!links.TryGetValue(linkId, out var link)
                    || link.TargetType != DocumentLinkTargetType.Sheet
                    || link.TargetId != sheetId
                    || link.DocumentId != relation.SourceId
                    || anchor.DocumentId is { } anchorDocumentId && anchorDocumentId != link.DocumentId
                    || !documents.TryGetValue(link.DocumentId, out var document))
                {
                    continue;
                }

                var hasRect = TryReadRect(anchor, out var rect);
                sources.Add(new PdfContentSource(
                    relation.Id,
                    document.Id,
                    document.Name,
                    ReadLocalPath(document),
                    anchor.PdfPage.Value,
                    hasRect ? rect : null,
                    relation.CreatedAt));
            }
        }

        return sources
            .OrderByDescending(source => source.CreatedAt)
            .ThenBy(source => source.RelationId, StringComparer.Ordinal)
            .ToList();
    }

    private static bool TryNumber(JsonElement source, string name, out double value)
    {
        value = 0d;
        return source.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out value)
            && double.IsFinite(value);
    }

    private static string? ReadLocalPath(DocumentRecord document)
    {
        if (document.Payload.ValueKind != JsonValueKind.Object
            || !document.Payload.TryGetProperty("localPath", out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }
}
