using System.Globalization;

namespace MedNote.Core;

/// <summary>
/// Exact C# port of the web app's FNV-1a stableHash implementation. Do not
/// change this algorithm: web and Windows must derive the same Document ID.
/// </summary>
public static class DocumentIdentity
{
    public static string Create(string fileName, long size, long lastModifiedUnixMilliseconds)
    {
        var fingerprint = string.Concat(
            fileName,
            ":",
            size.ToString(CultureInfo.InvariantCulture),
            ":",
            lastModifiedUnixMilliseconds.ToString(CultureInfo.InvariantCulture));
        return $"doc-{StableHash(fingerprint)}";
    }

    public static string StableHash(string value)
    {
        var hash = 2_166_136_261u;
        foreach (var codeUnit in value)
        {
            hash ^= codeUnit;
            hash = unchecked(hash * 16_777_619u);
        }

        return ToBase36(hash);
    }

    private static string ToBase36(uint value)
    {
        const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
        Span<char> buffer = stackalloc char[7];
        var cursor = buffer.Length;
        do
        {
            buffer[--cursor] = alphabet[(int)(value % 36)];
            value /= 36;
        }
        while (value > 0);

        return new string(buffer[cursor..]);
    }
}
