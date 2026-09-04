using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedNote.Core;

namespace MedNote.Infrastructure;

public sealed partial class FileNoteRepository
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private async Task ReplaceLibraryCoreAsync(NativeLibrarySnapshot library, CancellationToken cancellationToken)
    {
        NoteLibraryValidator.AssertValid(library);
        Directory.CreateDirectory(_rootPath);
        Directory.CreateDirectory(_blobPath);
        var references = new Dictionary<string, SheetBlobReference>(StringComparer.Ordinal);
        foreach (var sheet in library.Notes.Sheets)
        {
            references[sheet.Id] = await WriteSheetBlobAsync(library.SheetContents[sheet.Id], cancellationToken);
        }

        var manifest = new FileLibraryManifest
        {
            StoreFormatVersion = StoreFormatVersion,
            Version = NativeNoteSchema.Version,
            Notes = Clone(library.Notes),
            Documents = Clone(library.Documents),
            Preferences = Clone(library.Preferences),
            SavedAt = library.SavedAt,
            SheetBlobs = references,
            LibraryExtensionData = Clone(library.ExtensionData),
        };
        var stagedPath = TemporaryManifestPath();
        try
        {
            await WriteJsonFileAsync(stagedPath, manifest, _manifestJson, cancellationToken);
            var staged = await ReadManifestFileAsync(stagedPath, cancellationToken);
            ValidateManifest(staged, requireBlobFiles: true);
            var reloaded = await LoadLibraryAsync(staged, cancellationToken);
            NativeLibrarySnapshotVerifier.AssertEquivalent(library, reloaded);
            File.Move(stagedPath, _manifestPath, overwrite: true);
            CollectUnreferencedBlobs(staged);
        }
        catch
        {
            TryDelete(stagedPath);
            throw;
        }
    }

    private async Task CommitManifestAsync(FileLibraryManifest manifest, CancellationToken cancellationToken)
    {
        ValidateManifest(manifest, requireBlobFiles: true);
        Directory.CreateDirectory(_rootPath);
        var stagedPath = TemporaryManifestPath();
        try
        {
            await WriteJsonFileAsync(stagedPath, manifest, _manifestJson, cancellationToken);
            var reloaded = await ReadManifestFileAsync(stagedPath, cancellationToken);
            ValidateManifest(reloaded, requireBlobFiles: true);
            File.Move(stagedPath, _manifestPath, overwrite: true);
            CollectUnreferencedBlobs(reloaded);
        }
        catch
        {
            TryDelete(stagedPath);
            throw;
        }
    }

    private async Task<FileLibraryManifest?> ReadManifestAsync(bool required, CancellationToken cancellationToken)
    {
        if (!File.Exists(_manifestPath))
        {
            if (required)
            {
                throw new NoteRepositoryMutationException("Chưa có thư viện note native.");
            }

            return null;
        }

        var manifest = await ReadManifestFileAsync(_manifestPath, cancellationToken);
        ValidateManifest(manifest, requireBlobFiles: false);
        return manifest;
    }

    private async Task<FileLibraryManifest> RequireManifestAsync(CancellationToken cancellationToken) =>
        await ReadManifestAsync(required: true, cancellationToken)
        ?? throw new NoteRepositoryMutationException("Chưa có thư viện note native.");

    private async Task<FileLibraryManifest> ReadManifestFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<FileLibraryManifest>(stream, _manifestJson, cancellationToken)
                ?? throw new InvalidDataException("Manifest thư viện native rỗng.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Manifest thư viện native bị hỏng.", exception);
        }
    }

    private async Task<NativeLibrarySnapshot> LoadLibraryAsync(FileLibraryManifest manifest, CancellationToken cancellationToken)
    {
        var contents = new Dictionary<string, RtfSheetContent>(StringComparer.Ordinal);
        foreach (var sheet in manifest.Notes.Sheets)
        {
            contents[sheet.Id] = await ReadSheetContentAsync(sheet.Id, manifest.SheetBlobs[sheet.Id], cancellationToken);
        }

        var library = new NativeLibrarySnapshot
        {
            Notes = Clone(manifest.Notes),
            SheetContents = contents,
            Documents = Clone(manifest.Documents),
            Preferences = Clone(manifest.Preferences),
            SavedAt = manifest.SavedAt,
            ExtensionData = Clone(manifest.LibraryExtensionData),
        };
        NoteLibraryValidator.AssertValid(library);
        return library;
    }

    private async Task<RtfSheetContent> ReadSheetContentAsync(
        string sheetId,
        SheetBlobReference reference,
        CancellationToken cancellationToken)
    {
        var path = BlobFilePath(reference.Sha256);
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"SheetContent {sheetId} thiếu blob RTF {reference.Sha256}.");
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (bytes.LongLength != reference.ByteLength || actualHash != reference.Sha256)
            {
                throw new InvalidDataException($"Blob RTF của Sheet {sheetId} không khớp manifest.");
            }

            var content = new RtfSheetContent { Rtf = StrictUtf8.GetString(bytes) };
            NoteLibraryValidator.AssertSheetContentValid(sheetId, content);
            return content;
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException($"SheetContent {sheetId} không phải UTF-8 hợp lệ.", exception);
        }
    }

    private async Task<SheetBlobReference> WriteSheetBlobAsync(RtfSheetContent content, CancellationToken cancellationToken)
    {
        NoteLibraryValidator.AssertSheetContentValid(string.Empty, content);
        Directory.CreateDirectory(_blobPath);
        var bytes = StrictUtf8.GetBytes(content.Rtf);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var finalPath = BlobFilePath(sha256);
        var existingMatches = File.Exists(finalPath)
            && (await File.ReadAllBytesAsync(finalPath, cancellationToken)).AsSpan().SequenceEqual(bytes);
        if (!existingMatches)
        {
            var temporaryPath = Path.Combine(_blobPath, $".blob-{Guid.NewGuid():N}.tmp");
            try
            {
                await WriteBytesAsync(temporaryPath, bytes, cancellationToken);
                File.Move(temporaryPath, finalPath, overwrite: true);
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }

        return new SheetBlobReference
        {
            Format = NativeNoteSchema.RtfContentFormat,
            Sha256 = sha256,
            ByteLength = bytes.LongLength,
        };
    }

    private void ValidateManifest(FileLibraryManifest manifest, bool requireBlobFiles)
    {
        if (manifest.StoreFormatVersion != StoreFormatVersion || manifest.Version != NativeNoteSchema.Version)
        {
            throw new InvalidDataException("Manifest cục bộ không đúng phiên bản native.");
        }

        if (manifest.Notes is null
            || manifest.Documents is null
            || manifest.Preferences is null
            || manifest.SheetBlobs is null
            || manifest.LibraryExtensionData is null)
        {
            throw new InvalidDataException("Manifest cục bộ thiếu record bắt buộc.");
        }

        NoteLibraryValidator.AssertMetadataValid(
            manifest.Notes,
            manifest.Documents,
            manifest.Preferences,
            manifest.SheetBlobs.Keys);
        foreach (var (sheetId, reference) in manifest.SheetBlobs)
        {
            if (reference is null
                || reference.Format != NativeNoteSchema.RtfContentFormat
                || reference.ByteLength <= 0
                || !IsSafeSha256(reference.Sha256))
            {
                throw new InvalidDataException($"Blob reference của Sheet {sheetId} không hợp lệ.");
            }

            if (requireBlobFiles && !File.Exists(BlobFilePath(reference.Sha256)))
            {
                throw new InvalidDataException($"Sheet {sheetId} tham chiếu blob không tồn tại.");
            }
        }
    }

    private static FileLibraryManifest Touch(FileLibraryManifest manifest) => manifest with
    {
        SavedAt = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), manifest.SavedAt + 1),
    };

    private async ValueTask<T> LockedAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            return await operation();
        }
        finally
        {
            _gate.Release();
        }
    }

    private T Clone<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, _compactJson);
        return JsonSerializer.Deserialize<T>(bytes, _compactJson)
            ?? throw new InvalidDataException("Không thể sao chép record native.");
    }

    private async Task WriteJsonFileAsync<T>(
        string path,
        T value,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, value, options, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static async Task WriteBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private string TemporaryManifestPath() => Path.Combine(_rootPath, $".manifest-{Guid.NewGuid():N}.tmp");

    private string BlobFilePath(string sha256) => Path.Combine(_blobPath, $"{sha256}.rtf");

    private static bool IsSafeSha256(string? value) =>
        value is not null
        && value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private void CollectUnreferencedBlobs(FileLibraryManifest manifest)
    {
        try
        {
            if (!Directory.Exists(_blobPath))
            {
                return;
            }

            var referenced = manifest.SheetBlobs.Values.Select(item => $"{item.Sha256}.rtf").ToHashSet(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles(_blobPath, "*.rtf", SearchOption.TopDirectoryOnly))
            {
                if (!referenced.Contains(Path.GetFileName(path)))
                {
                    TryDelete(path);
                }
            }
        }
        catch
        {
            // Orphan immutable blobs are safe and can be collected after a later commit.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A stale staging file never becomes current without the manifest swap.
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed record FileLibraryManifest
    {
        [JsonConstructor]
        public FileLibraryManifest()
        {
        }

        [JsonRequired]
        public int StoreFormatVersion { get; init; }

        [JsonRequired]
        public int Version { get; init; }

        [JsonRequired]
        public NoteStructure Notes { get; init; } = new();

        [JsonRequired]
        public DocumentGraph Documents { get; init; } = new();

        [JsonRequired]
        public LibraryPreferences Preferences { get; init; } = new();

        [JsonRequired]
        public long SavedAt { get; init; }

        [JsonRequired]
        public Dictionary<string, SheetBlobReference> SheetBlobs { get; init; } = [];

        [JsonRequired]
        public Dictionary<string, JsonElement> LibraryExtensionData { get; init; } = [];
    }

    private sealed record SheetBlobReference
    {
        [JsonConstructor]
        public SheetBlobReference()
        {
        }

        [JsonRequired]
        public string Format { get; init; } = string.Empty;

        [JsonRequired]
        public string Sha256 { get; init; } = string.Empty;

        [JsonRequired]
        public long ByteLength { get; init; }
    }
}
