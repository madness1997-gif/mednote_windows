namespace MedNote.Core;

public sealed record CreateNotebookRequest
{
    public string? Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? SectionId { get; init; }

    public string? SectionTitle { get; init; }

    public string? PageId { get; init; }

    public string? PageTitle { get; init; }

    public string? SheetId { get; init; }
}

public sealed record CreateSectionRequest
{
    public string? Id { get; init; }

    public string NotebookId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;
}

public sealed record CreatePageRequest
{
    public string? Id { get; init; }

    public string SectionId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string? SheetId { get; init; }
}

public sealed record CreateSheetRequest
{
    public string? Id { get; init; }

    public string PageId { get; init; } = string.Empty;
}

public sealed record HierarchyMutation(
    NoteStructure Notes,
    IReadOnlyList<string> CreatedSheetIds,
    IReadOnlyList<string> RemovedSheetIds);

public sealed class NoteRepositoryMutationException : InvalidOperationException
{
    public NoteRepositoryMutationException(string message)
        : base(message)
    {
    }
}
