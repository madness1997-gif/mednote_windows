using System.Text.Json.Serialization;

namespace MedNote.Core;

public sealed record ReaderDocumentRecord
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Path { get; init; }

    public long Size { get; init; }

    public long LastModified { get; init; }

    public ReaderState Reader { get; init; } = new();

    public ReaderPosition Position { get; init; } = new();

    [JsonExtensionData]
    public Dictionary<string, object?> ExtensionData { get; init; } = [];
}

public sealed record ReaderLibrary
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public List<ReaderDocumentRecord> Documents { get; init; } = [];

    public string? ActiveDocumentId { get; init; }

    public long SavedAt { get; init; }

    [JsonExtensionData]
    public Dictionary<string, object?> ExtensionData { get; init; } = [];

    public ReaderLibrary Upsert(ReaderDocumentRecord document) => this with
    {
        Documents = Documents.Where(item => item.Id != document.Id).Append(document).ToList(),
        ActiveDocumentId = document.Id,
        SavedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    };
}

public interface IReaderLibraryStore
{
    ValueTask<ReaderLibrary> LoadAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(ReaderLibrary library, CancellationToken cancellationToken = default);
}
