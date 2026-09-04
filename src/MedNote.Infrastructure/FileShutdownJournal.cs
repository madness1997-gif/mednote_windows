using System.Text;
using System.Text.Json;
using MedNote.Core;

namespace MedNote.Infrastructure;

public sealed class FileShutdownJournal(string path) : IShutdownJournal
{
    private readonly string _path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));

    public bool HasInterruptedShutdown => File.Exists(_path);

    public async ValueTask BeginAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Đường dẫn journal shutdown không có thư mục.");
        Directory.CreateDirectory(directory);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            startedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        }, JsonDefaults.Create(writeIndented: true));
        await WriteAtomicAsync(_path, payload, cancellationToken);
    }

    public ValueTask CompleteAsync(CancellationToken cancellationToken = default)
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

    internal static async Task WriteAtomicAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Đường dẫn tệp không có thư mục.");
        Directory.CreateDirectory(directory);
        var staged = Path.Combine(directory, $".{Path.GetFileName(path)}-{Guid.NewGuid():N}.tmp");
        try
        {
            await using var stream = new FileStream(
                staged,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await stream.WriteAsync(content, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
            File.Move(staged, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(staged);
            }
            catch
            {
            }

            throw;
        }
    }
}
