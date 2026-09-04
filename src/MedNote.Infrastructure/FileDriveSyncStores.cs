using System.Text.Json;
using MedNote.Core;

namespace MedNote.Infrastructure;

public sealed class FileDriveSyncStateStore(string path) : IDriveSyncStateStore
{
    private readonly string _path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
    private readonly JsonSerializerOptions _json = JsonDefaults.Create(writeIndented: true);

    public async ValueTask<DriveSyncState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<DriveSyncState>(stream, _json, cancellationToken)
                ?? throw new InvalidDataException("Trạng thái Drive rỗng.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Trạng thái Drive bị hỏng.", exception);
        }
    }

    public async ValueTask SaveAsync(DriveSyncState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, _json);
        await FileShutdownJournal.WriteAtomicAsync(_path, bytes, cancellationToken);
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            File.Delete(_path);
        }
        catch (DirectoryNotFoundException)
        {
        }

        return ValueTask.CompletedTask;
    }
}

public sealed class FileDriveConflictArchive(string directoryPath) : IDriveConflictArchive
{
    private readonly string _directoryPath = Path.GetFullPath(directoryPath ?? throw new ArgumentNullException(nameof(directoryPath)));

    public async ValueTask SaveAsync(
        string localHash,
        byte[] localBackup,
        string remoteHash,
        byte[] remoteBackup,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directoryPath);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var prefix = Path.Combine(_directoryPath, $"conflict-{stamp}");
        await FileShutdownJournal.WriteAtomicAsync(
            $"{prefix}-local-{ShortHash(localHash)}.json",
            localBackup,
            cancellationToken);
        await FileShutdownJournal.WriteAtomicAsync(
            $"{prefix}-drive-{ShortHash(remoteHash)}.json",
            remoteBackup,
            cancellationToken);
    }

    private static string ShortHash(string value) => value.Length >= 12 ? value[..12] : "unknown";
}
