using System.Text.Json;

namespace MedNote.Core;

public static class NativeLibrarySnapshotVerifier
{
    private static readonly JsonSerializerOptions Json = JsonDefaults.Create();

    public static void AssertEquivalent(NativeLibrarySnapshot expected, NativeLibrarySnapshot actual)
    {
        NoteLibraryValidator.AssertValid(expected);
        NoteLibraryValidator.AssertValid(actual);
        var expectedJson = JsonSerializer.SerializeToElement(expected, Json);
        var actualJson = JsonSerializer.SerializeToElement(actual, Json);
        if (!JsonElement.DeepEquals(expectedJson, actualJson))
        {
            throw new InvalidDataException("Round-trip không giữ nguyên dữ liệu thư viện native.");
        }
    }
}
