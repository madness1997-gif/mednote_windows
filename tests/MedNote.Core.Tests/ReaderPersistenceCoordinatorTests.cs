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

    [TestMethod]
    public async Task QueueSave_DebouncesToLatestSnapshot()
    {
        var store = new MemoryStore(new ReaderLibrary());
        await using var persistence = new ReaderPersistenceCoordinator(store);
        await persistence.LoadAsync();

        persistence.QueueSave(Document("doc-1") with { Reader = new ReaderState { Page = 3 } });
        persistence.QueueSave(Document("doc-1") with { Reader = new ReaderState { Page = 9 } });
        await store.FirstSave.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, store.SaveCount);
        Assert.AreEqual(9, store.Value.Documents.Single().Reader.Page);
    }

    [TestMethod]
    public async Task SaveNow_CancelsOlderDebouncedSnapshot()
    {
        var store = new MemoryStore(new ReaderLibrary());
        await using var persistence = new ReaderPersistenceCoordinator(store);
        await persistence.LoadAsync();

        persistence.QueueSave(Document("doc-1") with { Reader = new ReaderState { Page = 2 } });
        await persistence.SaveNowAsync(Document("doc-1") with { Reader = new ReaderState { Page = 7 } });

        Assert.AreEqual(1, store.SaveCount);
        Assert.AreEqual(7, store.Value.Documents.Single().Reader.Page);
    }

    [TestMethod]
    public async Task Dispose_CancelsAndObservesPendingSave()
    {
        var store = new MemoryStore(new ReaderLibrary());
        var persistence = new ReaderPersistenceCoordinator(store);
        await persistence.LoadAsync();
        persistence.QueueSave(Document("doc-1"));

        await persistence.DisposeAsync();

        Assert.AreEqual(0, store.SaveCount);
        Assert.IsTrue(store.Disposed);
    }

    private static ReaderDocumentRecord Document(string id) => new()
    {
        Id = id,
        Name = "Harrison.pdf",
        Path = @"C:\Books\Harrison.pdf",
        Size = 10,
        LastModified = 20,
    };

    private sealed class MemoryStore(ReaderLibrary initial) : IReaderLibraryStore, IDisposable
    {
        private readonly TaskCompletionSource _firstSave = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ReaderLibrary Value { get; private set; } = initial;

        public int SaveCount { get; private set; }

        public bool Disposed { get; private set; }

        public Task FirstSave => _firstSave.Task;

        public ValueTask<ReaderLibrary> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Value);

        public ValueTask SaveAsync(ReaderLibrary library, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            Value = library;
            _firstSave.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public void Dispose() => Disposed = true;
    }
}
