using System.Text.Json;
using MedNote.Core;
using MedNote.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class NativeWorkspaceBootstrapTests
{
    [TestMethod]
    public async Task ReaderCutoverStore_UsesNativeOnlyAfterExplicitActivation()
    {
        var legacy = new MemoryReaderStore(new ReaderLibrary().Upsert(ReaderDocument("legacy")));
        var native = new MemoryReaderStore(new ReaderLibrary().Upsert(ReaderDocument("native")));
        var store = new ReaderLibraryCutoverStore(legacy, native);

        Assert.AreEqual("legacy", (await store.LoadAsync()).Documents.Single().Id);
        Assert.IsFalse(store.NativeActive);

        store.ActivateNative();
        await store.SaveAsync(new ReaderLibrary().Upsert(ReaderDocument("updated-native")));

        Assert.IsTrue(store.NativeActive);
        Assert.AreEqual("updated-native", (await store.LoadAsync()).Documents.Single().Id);
        Assert.AreEqual(0, legacy.SaveCount);
        Assert.AreEqual(1, native.SaveCount);
    }

    [TestMethod]
    public async Task Bootstrap_MigratesReaderV1AndCreatesFirstAidPage()
    {
        using var directory = new TemporaryRepositoryDirectory();
        await using var repository = new FileNoteRepository(directory.Path);
        var legacy = new MemoryReaderStore(new ReaderLibrary().Upsert(ReaderDocument("doc-1")));

        var result = await new NativeLibraryBootstrapper(repository).InitializeAsync(legacy);
        var library = await repository.LoadLibraryAsync();

        Assert.IsTrue(result.MigratedReaderV1);
        Assert.IsTrue(result.CreatedDefaultNote);
        Assert.IsNotNull(library);
        Assert.AreEqual(1, library.Notes.Sheets.Count);
        Assert.IsTrue(RtfDocument.IsRtf(library.SheetContents.Values.Single().Rtf));
        StringAssert.Contains(library.SheetContents.Values.Single().Rtf, "FIRST AID");
        Assert.AreEqual("doc-1", library.Documents.Documents.Single().Id);
        Assert.AreEqual(0, legacy.SaveCount);
    }

    [TestMethod]
    public async Task Bootstrap_LeavesExistingNativeLibraryUntouched()
    {
        using var directory = new TemporaryRepositoryDirectory();
        await using var repository = new FileNoteRepository(directory.Path);
        var expected = NoteLibraryTestData.Create(2);
        await repository.ReplaceLibraryAsync(expected);
        var legacy = new MemoryReaderStore(new ReaderLibrary().Upsert(ReaderDocument("legacy")));

        var result = await new NativeLibraryBootstrapper(repository).InitializeAsync(legacy);
        var actual = await repository.LoadLibraryAsync();

        Assert.IsFalse(result.MigratedReaderV1);
        Assert.IsFalse(result.CreatedDefaultNote);
        Assert.IsNotNull(actual);
        NativeLibrarySnapshotVerifier.AssertEquivalent(expected, actual);
        Assert.AreEqual(0, legacy.LoadCount);
    }

    [TestMethod]
    public async Task NativeReaderStore_RoundTripsStateAndPreservesForeignPayloadFields()
    {
        using var directory = new TemporaryRepositoryDirectory();
        await using var repository = new FileNoteRepository(directory.Path);
        var legacy = new MemoryReaderStore(new ReaderLibrary().Upsert(ReaderDocument("doc-1")));
        await new NativeLibraryBootstrapper(repository).InitializeAsync(legacy);
        var initialGraph = await repository.LoadDocumentGraphAsync();
        Assert.IsNotNull(initialGraph);
        await repository.ReplaceDocumentGraphAsync(initialGraph with
        {
            Documents =
            [
                .. initialGraph.Documents,
                new DocumentRecord
                {
                    Id = "foreign-doc",
                    Name = "Không thuộc Reader",
                    Available = false,
                    Payload = JsonSerializer.SerializeToElement(new { owner = "future" }),
                },
            ],
            Contexts =
            [
                .. initialGraph.Contexts,
                new DocumentContextRecord
                {
                    Id = "foreign-context",
                    Kind = "collection",
                    Name = "Future context",
                    DocumentIds = ["foreign-doc"],
                    ActiveDocumentId = "foreign-doc",
                },
            ],
        });
        var store = new NativeReaderLibraryStore(repository);
        var loaded = await store.LoadAsync();
        var document = loaded.Documents.Single() with
        {
            Reader = new ReaderState { Page = 18, Zoom = 1.25 },
            Position = new ReaderPosition { AnchorPage = 18, PageOffsetRatio = 0.42 },
        };

        await store.SaveAsync(loaded.Upsert(document));
        var restored = await store.LoadAsync();
        var graph = await repository.LoadDocumentGraphAsync();

        Assert.AreEqual(18, restored.Documents.Single().Reader.Page);
        Assert.AreEqual(0.42, restored.Documents.Single().Position.PageOffsetRatio, 0.001);
        Assert.AreEqual("kept", ((JsonElement)restored.Documents.Single().ExtensionData["futureField"]!).GetString());
        Assert.IsNotNull(graph);
        Assert.AreEqual(18, graph.Contexts.Single(context => context.Id == ReaderV1Migration.ContextId).SourcePage);
        Assert.IsTrue(graph.Documents.Any(document => document.Id == "foreign-doc"));
        Assert.IsTrue(graph.Contexts.Any(context => context.Id == "foreign-context"));
    }

    [TestMethod]
    public async Task NativeReaderStore_LoadDoesNotHydrateNoteBlobs()
    {
        using var directory = new TemporaryRepositoryDirectory();
        await using var repository = new FileNoteRepository(directory.Path);
        var legacy = new MemoryReaderStore(new ReaderLibrary().Upsert(ReaderDocument("doc-1")));
        await new NativeLibraryBootstrapper(repository).InitializeAsync(legacy);
        var blob = Directory.GetFiles(
            System.IO.Path.Combine(directory.Path, FileNoteRepository.BlobDirectoryName),
            "*.rtf").Single();
        File.Delete(blob);

        var reader = await new NativeReaderLibraryStore(repository).LoadAsync();

        Assert.AreEqual("doc-1", reader.Documents.Single().Id);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
        {
            _ = await repository.LoadLibraryAsync();
        });
    }

    private static ReaderDocumentRecord ReaderDocument(string id) => new()
    {
        Id = id,
        Name = "Harrison.pdf",
        Path = @"C:\Books\Harrison.pdf",
        Size = 10,
        LastModified = 20,
        Reader = new ReaderState { Page = 7 },
        Position = new ReaderPosition { AnchorPage = 7, PageOffsetRatio = 0.2 },
        ExtensionData = new Dictionary<string, object?> { ["futureField"] = "kept" },
    };

    private sealed class MemoryReaderStore(ReaderLibrary value) : IReaderLibraryStore
    {
        public int LoadCount { get; private set; }

        public int SaveCount { get; private set; }

        public ValueTask<ReaderLibrary> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return ValueTask.FromResult(value);
        }

        public ValueTask SaveAsync(ReaderLibrary library, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            value = library;
            return ValueTask.CompletedTask;
        }
    }
}
