namespace MedNote.Core;

public static class DocumentGraphOperations
{
    public static DocumentGraph SaveWorkspace(DocumentGraph source, SaveDocumentWorkspaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        return source with
        {
            Documents = Merge(source.Documents, request.Documents, record => record.Id),
            Contexts = Merge(source.Contexts, [request.Context], record => record.Id),
            Groups = Merge(source.Groups, request.Group is null ? [] : [request.Group], record => record.Id),
            Links = Merge(source.Links, request.Links, record => record.Id),
            LinkRelations = Merge(source.LinkRelations, request.LinkRelations, record => record.Id),
        };
    }

    public static DocumentGraph DeleteWorkspace(DocumentGraph source, string contextId)
    {
        var removedContext = source.Contexts.FirstOrDefault(context => context.Id == contextId);
        if (removedContext is null)
        {
            return source;
        }

        var contexts = source.Contexts.Where(context => context.Id != contextId).ToList();
        var removedGroupIds = source.Groups.Where(group => group.Id == contextId).Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        var groups = source.Groups.Where(group => !removedGroupIds.Contains(group.Id)).ToList();
        var referencedDocumentIds = contexts.SelectMany(context => context.DocumentIds)
            .Concat(groups.SelectMany(group => group.DocumentIds))
            .ToHashSet(StringComparer.Ordinal);
        var removedDocumentIds = removedContext.DocumentIds.Where(id => !referencedDocumentIds.Contains(id)).ToHashSet(StringComparer.Ordinal);
        var documents = source.Documents.Where(document => !removedDocumentIds.Contains(document.Id)).ToList();
        var links = source.Links.Where(link => !removedDocumentIds.Contains(link.DocumentId)).ToList();
        var linkIds = links.Select(link => link.Id).ToHashSet(StringComparer.Ordinal);
        var relations = RepairRelations(source.LinkRelations, linkIds)
            .Where(relation => relation.SourceType != DocumentRelationSourceType.Group || !removedGroupIds.Contains(relation.SourceId))
            .Where(relation => relation.SourceType != DocumentRelationSourceType.Document || !removedDocumentIds.Contains(relation.SourceId))
            .ToList();
        return source with
        {
            Documents = documents,
            Contexts = contexts,
            Groups = groups,
            Links = links,
            LinkRelations = relations,
        };
    }

    public static DocumentGraph DeleteDocumentFromWorkspace(DocumentGraph source, string contextId, string documentId, long? updatedAt = null)
    {
        var target = source.Contexts.FirstOrDefault(context => context.Id == contextId);
        if (target is null || !target.DocumentIds.Contains(documentId, StringComparer.Ordinal))
        {
            return source;
        }

        if (target.DocumentIds.Count <= 1)
        {
            throw new NoteRepositoryMutationException("Context phải còn ít nhất một Document; hãy xóa cả workspace thay vì xóa Document cuối.");
        }

        var now = updatedAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nextDocumentIds = target.DocumentIds.Where(id => id != documentId).ToList();
        var contexts = source.Contexts.Select(context => context.Id == contextId
            ? context with
            {
                DocumentIds = nextDocumentIds,
                ActiveDocumentId = context.ActiveDocumentId == documentId ? nextDocumentIds[0] : context.ActiveDocumentId,
            }
            : context).ToList();
        var groups = source.Groups.Select(group => group.Id == contextId
            ? group with
            {
                DocumentIds = group.DocumentIds.Where(id => id != documentId).ToList(),
                UpdatedAt = now,
            }
            : group).ToList();
        var referencedDocumentIds = contexts.SelectMany(context => context.DocumentIds)
            .Concat(groups.SelectMany(group => group.DocumentIds))
            .ToHashSet(StringComparer.Ordinal);
        var removeDocument = !referencedDocumentIds.Contains(documentId);
        var documents = removeDocument ? source.Documents.Where(document => document.Id != documentId).ToList() : source.Documents;
        var links = removeDocument ? source.Links.Where(link => link.DocumentId != documentId).ToList() : source.Links;
        var linksById = links.ToDictionary(link => link.Id, StringComparer.Ordinal);
        var groupDocuments = groups.FirstOrDefault(group => group.Id == contextId)?.DocumentIds.ToHashSet(StringComparer.Ordinal)
            ?? nextDocumentIds.ToHashSet(StringComparer.Ordinal);
        var relations = source.LinkRelations.Select(relation =>
        {
            if (removeDocument && relation.SourceType == DocumentRelationSourceType.Document && relation.SourceId == documentId)
            {
                return null;
            }

            var relationLinkIds = relation.LinkIds.Where(linksById.ContainsKey).ToList();
            if (relation.SourceType == DocumentRelationSourceType.Group && relation.SourceId == contextId)
            {
                relationLinkIds = relationLinkIds.Where(id => groupDocuments.Contains(linksById[id].DocumentId)).ToList();
            }

            return relationLinkIds.Count == 0 ? null : relation with { LinkIds = relationLinkIds, UpdatedAt = now };
        }).Where(relation => relation is not null).Select(relation => relation!).ToList();
        return source with
        {
            Documents = documents,
            Contexts = contexts,
            Groups = groups,
            Links = links,
            LinkRelations = relations,
        };
    }

    public static DocumentGraph UpsertDocument(DocumentGraph source, DocumentRecord document) => source with
    {
        Documents = Upsert(source.Documents, document, record => record.Id),
    };

    public static DocumentGraph UpsertContext(DocumentGraph source, DocumentContextRecord context) => source with
    {
        Contexts = Upsert(source.Contexts, context, record => record.Id),
    };

    public static DocumentGraph UpsertGroup(DocumentGraph source, DocumentGroupRecord group) => source with
    {
        Groups = Upsert(source.Groups, group, record => record.Id),
    };

    public static DocumentGraph UpsertLink(DocumentGraph source, NoteDocumentLink link) => source with
    {
        Links = Upsert(source.Links, link, record => record.Id),
    };

    public static DocumentGraph UpsertLinkRelation(DocumentGraph source, DocumentLinkRelation relation) => source with
    {
        LinkRelations = Upsert(source.LinkRelations, relation, record => record.Id),
    };

    public static DocumentGraph UpsertContentLink(
        DocumentGraph source,
        NoteDocumentLink link,
        DocumentLinkRelation relation)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(relation);
        if (string.IsNullOrWhiteSpace(link.Id)
            || string.IsNullOrWhiteSpace(link.DocumentId)
            || string.IsNullOrWhiteSpace(link.TargetId)
            || link.TargetType != DocumentLinkTargetType.Sheet
            || relation.Kind != DocumentRelationKind.Content
            || relation.SourceType != DocumentRelationSourceType.Document
            || relation.SourceId != link.DocumentId
            || relation.ContentAnchor is not { PdfPage: >= 1 } anchor
            || anchor.DocumentId != link.DocumentId
            || !PdfContentLinks.TryReadRect(anchor, out _)
            || relation.LinkIds is null
            || !relation.LinkIds.Contains(link.Id, StringComparer.Ordinal))
        {
            throw new NoteRepositoryMutationException("Liên kết nội dung PDF phải trỏ từ đúng Document tới một Sheet và chứa anchor trang hợp lệ.");
        }

        if (!source.Documents.Any(document => document.Id == link.DocumentId))
        {
            throw new NoteRepositoryMutationException($"Không tìm thấy Document nguồn {link.DocumentId}.");
        }

        return source with
        {
            Links = Upsert(source.Links, link, record => record.Id),
            LinkRelations = Upsert(source.LinkRelations, relation, record => record.Id),
        };
    }

    public static DocumentGraph RemoveLink(DocumentGraph source, string id)
    {
        var links = source.Links.Where(record => record.Id != id).ToList();
        return source with
        {
            Links = links,
            LinkRelations = RepairRelations(source.LinkRelations, links.Select(record => record.Id).ToHashSet(StringComparer.Ordinal)),
        };
    }

    public static DocumentGraph RemoveTargets(DocumentGraph source, IReadOnlySet<string> pageIds, IReadOnlySet<string> sheetIds)
    {
        var links = source.Links.Where(link => link.TargetType == DocumentLinkTargetType.Page
            ? !pageIds.Contains(link.TargetId)
            : !sheetIds.Contains(link.TargetId)).ToList();
        return source with
        {
            Links = links,
            LinkRelations = RepairRelations(source.LinkRelations, links.Select(record => record.Id).ToHashSet(StringComparer.Ordinal)),
        };
    }

    public static DocumentGraph DeleteDocument(DocumentGraph source, string documentId)
    {
        var documents = source.Documents.Where(record => record.Id != documentId).ToList();
        var contexts = source.Contexts.Select(context =>
        {
            var ids = context.DocumentIds.Where(id => id != documentId).ToList();
            return context with
            {
                DocumentIds = ids,
                ActiveDocumentId = context.ActiveDocumentId == documentId ? ids.FirstOrDefault() : context.ActiveDocumentId,
            };
        }).ToList();
        var groups = source.Groups.Select(group => group with
        {
            DocumentIds = group.DocumentIds.Where(id => id != documentId).ToList(),
        }).ToList();
        var links = source.Links.Where(record => record.DocumentId != documentId).ToList();
        var linkIds = links.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
        var groupIds = groups.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
        return source with
        {
            Documents = documents,
            Contexts = contexts,
            Groups = groups,
            Links = links,
            LinkRelations = RepairRelations(source.LinkRelations, linkIds)
                .Where(relation => relation.SourceType != DocumentRelationSourceType.Document || relation.SourceId != documentId)
                .Where(relation => relation.SourceType != DocumentRelationSourceType.Group || groupIds.Contains(relation.SourceId))
                .ToList(),
        };
    }

    private static List<DocumentLinkRelation> RepairRelations(
        IEnumerable<DocumentLinkRelation> relations,
        IReadOnlySet<string> linkIds) => relations.Select(relation => relation with
        {
            LinkIds = relation.LinkIds.Where(linkIds.Contains).ToList(),
        }).Where(relation => relation.LinkIds.Count > 0).ToList();

    private static List<T> Upsert<T>(IEnumerable<T> records, T incoming, Func<T, string> id)
    {
        var result = records.ToList();
        var index = result.FindIndex(record => id(record) == id(incoming));
        if (index < 0)
        {
            result.Add(incoming);
        }
        else
        {
            result[index] = incoming;
        }

        return result;
    }

    private static List<T> Merge<T>(IEnumerable<T> existing, IEnumerable<T> incoming, Func<T, string> id)
    {
        var result = existing.ToDictionary(id, item => item, StringComparer.Ordinal);
        foreach (var record in incoming)
        {
            result[id(record)] = record;
        }

        return result.Values.ToList();
    }
}
