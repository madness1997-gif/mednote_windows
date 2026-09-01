using System.Text.Json;
using MedNote.Core;

namespace MedNote.Windows.App.Infrastructure;

public sealed class JsonReaderLibraryStore : IReaderLibraryStore, IDisposable
{
    private readonly string _path;
    private readonly JsonSerializerOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonReaderLibraryStore(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MedNote Reader",
            "reader-library-v1.json");
        _options = JsonDefaults.Create(writeIndented: true);
    }

    public async ValueTask<ReaderLibrary> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path))
            {
                return new ReaderLibrary();
            }

            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var library = await JsonSerializer.DeserializeAsync<ReaderLibrary>(stream, _options, cancellationToken);
            if (library is null || library.Version != ReaderLibrary.CurrentVersion)
            {
                throw new InvalidDataException("Dữ liệu Reader cục bộ không đúng phiên bản.");
            }

            return library;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask SaveAsync(ReaderLibrary library, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        var temporaryPath = $"{_path}.tmp";
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, library, _options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
