using MedNote.Core;
using MedNote.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class DriveSyncCoordinatorTests
{
    [TestMethod]
    public async Task Sync_CreatesThenUploadsLocalChangeWithExpectedETag()
    {
        using var directory = new TemporaryRepositoryDirectory();
        await using var repository = new FileNoteRepository(directory.Path);
        await repository.ReplaceLibraryAsync(NoteLibraryTestData.Create(1));
        var remote = new MemoryRemote();
        var state = new MemoryState();
        var archive = new MemoryArchive();
        var sync = new DriveSyncCoordinator(repository, remote, state, archive);

        var created = await sync.SyncAsync();
        await repository.SaveSheetContentAsync("sheet-1", new RtfSheetContent { Rtf = @"{\rtf1\ansi local change}" });
        var uploaded = await sync.SyncAsync();

        Assert.AreEqual(DriveSyncOutcome.CreatedRemote, created.Outcome);
        Assert.AreEqual(DriveSyncOutcome.UploadedLocal, uploaded.Outcome);
        Assert.AreEqual("\"etag-1\"", remote.LastExpectedETag);
        Assert.AreEqual(0, archive.Count);
    }

    [TestMethod]
    public async Task Sync_RestoresRemoteWhenOnlyRemoteChanged()
    {
        using var directory = new TemporaryRepositoryDirectory();
        await using var repository = new FileNoteRepository(directory.Path);
        await repository.ReplaceLibraryAsync(NoteLibraryTestData.Create(1));
        var remote = new MemoryRemote();
        var state = new MemoryState();
        var sync = new DriveSyncCoordinator(repository, remote, state, new MemoryArchive());
        await sync.SyncAsync();
        var remoteLibrary = NoteLibraryTestData.Create(1) with
        {
            SheetContents = new Dictionary<string, RtfSheetContent>
            {
                ["sheet-1"] = new() { Rtf = @"{\rtf1\ansi remote change}" },
            },
            SavedAt = 20,
        };
        remote.Set(remoteLibrary);

        var result = await sync.SyncAsync();
        var restored = await repository.LoadLibraryAsync();

        Assert.AreEqual(DriveSyncOutcome.RestoredRemote, result.Outcome);
        Assert.AreEqual(@"{\rtf1\ansi remote change}", restored!.SheetContents["sheet-1"].Rtf);
    }

    [TestMethod]
    public async Task Sync_ArchivesBothSidesAndRequiresChoiceWhenBothChanged()
    {
        using var directory = new TemporaryRepositoryDirectory();
        await using var repository = new FileNoteRepository(directory.Path);
        await repository.ReplaceLibraryAsync(NoteLibraryTestData.Create(1));
        var remote = new MemoryRemote();
        var state = new MemoryState();
        var archive = new MemoryArchive();
        var sync = new DriveSyncCoordinator(repository, remote, state, archive);
        await sync.SyncAsync();
        await repository.SaveSheetContentAsync("sheet-1", new RtfSheetContent { Rtf = @"{\rtf1\ansi local}" });
        remote.Set(NoteLibraryTestData.Create(1) with
        {
            SheetContents = new Dictionary<string, RtfSheetContent>
            {
                ["sheet-1"] = new() { Rtf = @"{\rtf1\ansi remote}" },
            },
            SavedAt = 30,
        });

        var conflict = await sync.SyncAsync();
        var resolved = await sync.SyncAsync(DriveConflictResolution.UseLocal);

        Assert.AreEqual(DriveSyncOutcome.Conflict, conflict.Outcome);
        Assert.AreEqual(1, archive.Count);
        Assert.AreEqual(DriveSyncOutcome.UploadedLocal, resolved.Outcome);
        Assert.AreEqual(2, archive.Count);
    }

    private sealed class MemoryRemote : IDriveAppDataClient
    {
        private byte[]? _content;
        private int _version;

        public string? LastExpectedETag { get; private set; }

        public Task<DriveRemoteFile?> FindAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_content is null ? null : File());

        public Task<DriveDownloadedFile> DownloadAsync(string fileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DriveDownloadedFile { File = File(), Content = _content!.ToArray() });

        public Task<DriveRemoteFile> CreateAsync(string name, byte[] content, CancellationToken cancellationToken = default)
        {
            _content = content.ToArray();
            _version = 1;
            return Task.FromResult(File());
        }

        public Task<DriveRemoteFile> UpdateAsync(
            string fileId,
            string expectedETag,
            byte[] content,
            CancellationToken cancellationToken = default)
        {
            LastExpectedETag = expectedETag;
            if (expectedETag != File().ETag)
            {
                throw new DriveRemoteChangedException("changed");
            }

            _content = content.ToArray();
            _version++;
            return Task.FromResult(File());
        }

        public void Set(NativeLibrarySnapshot library)
        {
            _content = NativeDriveBackupCodec.Serialize(NativeDriveBackupCodec.Create(library));
            _version++;
        }

        private DriveRemoteFile File() => new()
        {
            Id = "remote-1",
            Name = DriveSyncCoordinator.BackupFileName,
            ETag = $"\"etag-{_version}\"",
            Version = _version.ToString(),
        };
    }

    private sealed class MemoryState : IDriveSyncStateStore
    {
        private DriveSyncState? _state;

        public ValueTask<DriveSyncState?> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_state);

        public ValueTask SaveAsync(DriveSyncState state, CancellationToken cancellationToken = default)
        {
            _state = state;
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(CancellationToken cancellationToken = default)
        {
            _state = null;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MemoryArchive : IDriveConflictArchive
    {
        public int Count { get; private set; }

        public ValueTask SaveAsync(
            string localHash,
            byte[] localBackup,
            string remoteHash,
            byte[] remoteBackup,
            CancellationToken cancellationToken = default)
        {
            Assert.IsTrue(localBackup.Length > 0);
            Assert.IsTrue(remoteBackup.Length > 0);
            Count++;
            return ValueTask.CompletedTask;
        }
    }
}
