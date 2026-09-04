using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MedNote.Core;

public static class NativeDriveBackupFormat
{
    public const string Name = "mednote-native-library-v2";

    public const int ManifestVersion = 2;

    public const string HashAlgorithm = "sha256-canonical-json-v1";
}

public sealed record NativeDriveBackupV2
{
    [JsonRequired]
    public string Format { get; init; } = NativeDriveBackupFormat.Name;

    [JsonRequired]
    public int ManifestVersion { get; init; } = NativeDriveBackupFormat.ManifestVersion;

    [JsonRequired]
    public int NativeSchemaVersion { get; init; } = NativeNoteSchema.Version;

    [JsonRequired]
    public long ExportedAt { get; init; }

    [JsonRequired]
    public string HashAlgorithm { get; init; } = NativeDriveBackupFormat.HashAlgorithm;

    [JsonRequired]
    public string LibrarySha256 { get; init; } = string.Empty;

    [JsonRequired]
    public Dictionary<string, string> SheetContentHashes { get; init; } = [];

    [JsonRequired]
    public NativeLibrarySnapshot Library { get; init; } = new();
}

public static class NativeDriveBackupCodec
{
    private static readonly JsonSerializerOptions Json = JsonDefaults.Create(writeIndented: true);

    public static NativeDriveBackupV2 Create(NativeLibrarySnapshot library, long? exportedAt = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        NoteLibraryValidator.AssertValid(library);
        var snapshot = Clone(library);
        return new NativeDriveBackupV2
        {
            ExportedAt = exportedAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            LibrarySha256 = ComputeLibraryHash(snapshot),
            SheetContentHashes = SheetHashes(snapshot),
            Library = snapshot,
        };
    }

    public static byte[] Serialize(NativeDriveBackupV2 backup)
    {
        AssertValid(backup);
        return JsonSerializer.SerializeToUtf8Bytes(backup, Json);
    }

    public static NativeDriveBackupV2 Parse(ReadOnlySpan<byte> payload)
    {
        NativeDriveBackupV2 backup;
        try
        {
            backup = JsonSerializer.Deserialize<NativeDriveBackupV2>(payload, Json)
                ?? throw new InvalidDataException("Bản lưu Drive rỗng.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Bản lưu Drive không phải JSON hợp lệ.", exception);
        }

        AssertValid(backup);
        return backup;
    }

    public static string ComputeLibraryHash(NativeLibrarySnapshot library)
    {
        ArgumentNullException.ThrowIfNull(library);
        var element = JsonSerializer.SerializeToElement(library, Json);
        return Sha256(CanonicalJson(element));
    }

    private static void AssertValid(NativeDriveBackupV2 backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        if (backup.Format != NativeDriveBackupFormat.Name
            || backup.ManifestVersion != NativeDriveBackupFormat.ManifestVersion
            || backup.NativeSchemaVersion != NativeNoteSchema.Version
            || backup.HashAlgorithm != NativeDriveBackupFormat.HashAlgorithm
            || backup.ExportedAt <= 0)
        {
            throw new InvalidDataException("Bản lưu Drive không phải manifest native v2.");
        }

        NoteLibraryValidator.AssertValid(backup.Library);
        var expectedSheets = SheetHashes(backup.Library);
        if (expectedSheets.Count != backup.SheetContentHashes.Count
            || expectedSheets.Any(item => !backup.SheetContentHashes.TryGetValue(item.Key, out var hash)
                || !IsSha256(hash)
                || !CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(item.Value),
                    Encoding.ASCII.GetBytes(hash))))
        {
            throw new InvalidDataException("Hash nội dung Sheet trong bản lưu Drive không khớp.");
        }

        var libraryHash = ComputeLibraryHash(backup.Library);
        if (!IsSha256(backup.LibrarySha256)
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(libraryHash),
                Encoding.ASCII.GetBytes(backup.LibrarySha256)))
        {
            throw new InvalidDataException("Hash thư viện trong bản lưu Drive không khớp.");
        }
    }

    private static Dictionary<string, string> SheetHashes(NativeLibrarySnapshot library) =>
        library.Notes.Sheets
            .OrderBy(sheet => sheet.Id, StringComparer.Ordinal)
            .ToDictionary(
                sheet => sheet.Id,
                sheet => Sha256(Encoding.UTF8.GetBytes(library.SheetContents[sheet.Id].Rtf)),
                StringComparer.Ordinal);

    private static byte[] CanonicalJson(JsonElement root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, root);
        }

        return stream.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static T Clone<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        return JsonSerializer.Deserialize<T>(bytes, Json)
            ?? throw new InvalidDataException("Không thể sao chép snapshot Drive.");
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool IsSha256(string? value) => value is not null
        && value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
