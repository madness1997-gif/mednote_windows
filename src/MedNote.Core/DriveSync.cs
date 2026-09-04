namespace MedNote.Core;

public sealed record DriveRemoteFile
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string ETag { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public long ModifiedAt { get; init; }
}

public sealed record DriveDownloadedFile
{
    public DriveRemoteFile File { get; init; } = new();

    public byte[] Content { get; init; } = [];
}

public interface IDriveAppDataClient
{
    Task<DriveRemoteFile?> FindAsync(string name, CancellationToken cancellationToken = default);

    Task<DriveDownloadedFile> DownloadAsync(string fileId, CancellationToken cancellationToken = default);

    Task<DriveRemoteFile> CreateAsync(string name, byte[] content, CancellationToken cancellationToken = default);

    Task<DriveRemoteFile> UpdateAsync(
        string fileId,
        string expectedETag,
        byte[] content,
        CancellationToken cancellationToken = default);
}

public sealed record DriveSyncState
{
    public string RemoteFileId { get; init; } = string.Empty;

    public string RemoteETag { get; init; } = string.Empty;

    public string LastSyncedLibraryHash { get; init; } = string.Empty;

    public long SyncedAt { get; init; }
}

public interface IDriveSyncStateStore
{
    ValueTask<DriveSyncState?> LoadAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(DriveSyncState state, CancellationToken cancellationToken = default);

    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}

public interface IDriveConflictArchive
{
    ValueTask SaveAsync(
        string localHash,
        byte[] localBackup,
        string remoteHash,
        byte[] remoteBackup,
        CancellationToken cancellationToken = default);
}

public enum DriveConflictResolution
{
    None,
    UseLocal,
    UseRemote,
}

public enum DriveSyncOutcome
{
    NoChanges,
    CreatedRemote,
    UploadedLocal,
    RestoredRemote,
    Conflict,
}

public sealed record DriveSyncResult
{
    public DriveSyncOutcome Outcome { get; init; }

    public string LocalHash { get; init; } = string.Empty;

    public string RemoteHash { get; init; } = string.Empty;

    public DriveRemoteFile? RemoteFile { get; init; }
}

public sealed class DriveRemoteChangedException(string message) : IOException(message);

/// <summary>
/// Synchronizes one native v2 manifest in Drive appDataFolder. A remote update
/// always carries an If-Match ETag, and divergent edits are archived before the
/// caller is asked to choose either side.
/// </summary>
public sealed class DriveSyncCoordinator(
    INoteRepository repository,
    IDriveAppDataClient remote,
    IDriveSyncStateStore stateStore,
    IDriveConflictArchive conflictArchive)
{
    public const string BackupFileName = "MedNote Native Library v2.json";

    private readonly INoteRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly IDriveAppDataClient _remote = remote ?? throw new ArgumentNullException(nameof(remote));
    private readonly IDriveSyncStateStore _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    private readonly IDriveConflictArchive _conflictArchive = conflictArchive ?? throw new ArgumentNullException(nameof(conflictArchive));
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<DriveSyncResult> SyncAsync(
        DriveConflictResolution resolution = DriveConflictResolution.None,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var local = await _repository.LoadLibraryAsync(cancellationToken)
                ?? throw new InvalidOperationException("Chưa có thư viện native để đồng bộ.");
            var localBackup = NativeDriveBackupCodec.Create(local);
            var localBytes = NativeDriveBackupCodec.Serialize(localBackup);
            var localHash = localBackup.LibrarySha256;
            var remoteFile = await _remote.FindAsync(BackupFileName, cancellationToken);
            if (remoteFile is null)
            {
                var created = await _remote.CreateAsync(BackupFileName, localBytes, cancellationToken);
                await SaveStateAsync(created, localHash, cancellationToken);
                return Result(DriveSyncOutcome.CreatedRemote, localHash, localHash, created);
            }

            var downloaded = await _remote.DownloadAsync(remoteFile.Id, cancellationToken);
            var remoteBackup = NativeDriveBackupCodec.Parse(downloaded.Content);
            remoteFile = downloaded.File;
            var remoteHash = remoteBackup.LibrarySha256;
            var state = await _stateStore.LoadAsync(cancellationToken);

            if (localHash == remoteHash)
            {
                await SaveStateAsync(remoteFile, localHash, cancellationToken);
                return Result(DriveSyncOutcome.NoChanges, localHash, remoteHash, remoteFile);
            }

            if (resolution == DriveConflictResolution.UseLocal)
            {
                await ArchiveAsync(localHash, localBytes, remoteHash, downloaded.Content, cancellationToken);
                return await UploadAsync(remoteFile, localBytes, localHash, remoteHash, cancellationToken);
            }

            if (resolution == DriveConflictResolution.UseRemote)
            {
                await ArchiveAsync(localHash, localBytes, remoteHash, downloaded.Content, cancellationToken);
                await RestoreAsync(remoteBackup.Library, remoteFile, remoteHash, cancellationToken);
                return Result(DriveSyncOutcome.RestoredRemote, localHash, remoteHash, remoteFile);
            }

            var baseline = state?.RemoteFileId == remoteFile.Id
                ? state.LastSyncedLibraryHash
                : null;
            if (!string.IsNullOrEmpty(baseline) && remoteHash == baseline && localHash != baseline)
            {
                return await UploadAsync(remoteFile, localBytes, localHash, remoteHash, cancellationToken);
            }

            if (!string.IsNullOrEmpty(baseline) && localHash == baseline && remoteHash != baseline)
            {
                await RestoreAsync(remoteBackup.Library, remoteFile, remoteHash, cancellationToken);
                return Result(DriveSyncOutcome.RestoredRemote, localHash, remoteHash, remoteFile);
            }

            await ArchiveAsync(localHash, localBytes, remoteHash, downloaded.Content, cancellationToken);
            return Result(DriveSyncOutcome.Conflict, localHash, remoteHash, remoteFile);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DriveSyncResult> UploadAsync(
        DriveRemoteFile remoteFile,
        byte[] localBytes,
        string localHash,
        string remoteHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(remoteFile.ETag))
        {
            throw new DriveRemoteChangedException("Google Drive không trả về ETag an toàn cho bản lưu.");
        }

        var uploaded = await _remote.UpdateAsync(remoteFile.Id, remoteFile.ETag, localBytes, cancellationToken);
        await SaveStateAsync(uploaded, localHash, cancellationToken);
        return Result(DriveSyncOutcome.UploadedLocal, localHash, remoteHash, uploaded);
    }

    private async Task RestoreAsync(
        NativeLibrarySnapshot remoteLibrary,
        DriveRemoteFile remoteFile,
        string remoteHash,
        CancellationToken cancellationToken)
    {
        await _repository.ReplaceLibraryAsync(remoteLibrary, cancellationToken);
        var reloaded = await _repository.LoadLibraryAsync(cancellationToken)
            ?? throw new InvalidDataException("Không đọc lại được thư viện sau khi khôi phục Drive.");
        NativeLibrarySnapshotVerifier.AssertEquivalent(remoteLibrary, reloaded);
        await SaveStateAsync(remoteFile, remoteHash, cancellationToken);
    }

    private ValueTask ArchiveAsync(
        string localHash,
        byte[] localBytes,
        string remoteHash,
        byte[] remoteBytes,
        CancellationToken cancellationToken) =>
        _conflictArchive.SaveAsync(localHash, localBytes, remoteHash, remoteBytes, cancellationToken);

    private ValueTask SaveStateAsync(DriveRemoteFile file, string libraryHash, CancellationToken cancellationToken) =>
        _stateStore.SaveAsync(new DriveSyncState
        {
            RemoteFileId = file.Id,
            RemoteETag = file.ETag,
            LastSyncedLibraryHash = libraryHash,
            SyncedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        }, cancellationToken);

    private static DriveSyncResult Result(
        DriveSyncOutcome outcome,
        string localHash,
        string remoteHash,
        DriveRemoteFile file) => new()
        {
            Outcome = outcome,
            LocalHash = localHash,
            RemoteHash = remoteHash,
            RemoteFile = file,
        };
}
