using System.Text.Json;
using System.Text.Json.Nodes;
using MedNote.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class NativeDriveBackupTests
{
    [TestMethod]
    public void RoundTrip_PreservesNativeSnapshotAndStableHash()
    {
        var expected = NoteLibraryTestData.WithLinkedDocument(NoteLibraryTestData.Create());
        var backup = NativeDriveBackupCodec.Create(expected, exportedAt: 100);
        var parsed = NativeDriveBackupCodec.Parse(NativeDriveBackupCodec.Serialize(backup));

        NativeLibrarySnapshotVerifier.AssertEquivalent(expected, parsed.Library);
        Assert.AreEqual(backup.LibrarySha256, parsed.LibrarySha256);
        Assert.AreEqual(2, parsed.SheetContentHashes.Count);
    }

    [TestMethod]
    public void Parse_RejectsContentChangedWithoutMatchingHashes()
    {
        var backup = NativeDriveBackupCodec.Create(NoteLibraryTestData.Create(), exportedAt: 100);
        var node = JsonNode.Parse(NativeDriveBackupCodec.Serialize(backup))!.AsObject();
        node["library"]!["sheetContents"]!["sheet-1"]!["rtf"] = @"{\rtf1\ansi tampered}";

        Assert.ThrowsExactly<InvalidDataException>(() =>
            NativeDriveBackupCodec.Parse(JsonSerializer.SerializeToUtf8Bytes(node, JsonDefaults.Create())));
    }
}
