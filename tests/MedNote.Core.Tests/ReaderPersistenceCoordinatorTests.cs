using MedNote.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class ReaderPersistenceCoordinatorTests
{
    [TestMethod]
    public async Task SaveNow_PreservesUnknownDocumentFields()
    {
        var original = Document("doc-1") with
        {
            ExtensionData = new Dictionary<string, object?> { ["futureField"] = "kept" },
        };
        var store = new MemoryStore(new ReaderLibrary().Upsert(original));
        await using var persistence = new ReaderPersistenceCoordinator(store);
        await persistence.LoadAsync();

        await persistence.SaveNowAsync(Document("doc-1") with
        {
            Reader = new ReaderState { Page = 20 },
        });

        var saved = store.Value.Documents.Single();
        Assert.AreEqual(20, saved.Reader.Page);
        Assert.AreEqual("kept", saved.ExtensionData["futureField"] as string);
        Assert.AreEqual("doc-1", store.Value.ActiveDocumentId);
    }

    private static ReaderDocumentRecord Document(string id) => new()
    {
        Id = id,
        Name = "Harrison.pdf",
        Path = @"C:\Books\Harrison.pdf",
        Size = 10,
        LastModified = 20,
    };

    private sealed class MemoryStore(ReaderLibrary initial) : IReaderLibraryStore
    {
        public ReaderLibrary Value { get; private set; } = initial;

        public ValueTask<ReaderLibrary> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Value);

        public ValueTask SaveAsync(ReaderLibrary library, CancellationToken cancellationToken = default)
        {
            Value = library;
            return ValueTask.CompletedTask;
        }
    }
}
