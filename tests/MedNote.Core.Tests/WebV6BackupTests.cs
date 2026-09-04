using System.Text.Json;
using MedNote.Core;
using MedNote.Core.Compatibility.WebV6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class WebV6BackupTests
{
    [TestMethod]
    public void ContentHash_MatchesWebJsonSafeFnvContract()
    {
        var first = JsonSerializer.SerializeToElement(new { body = "hello" });
        var second = JsonSerializer.SerializeToElement(new
        {
            nested = new { z = true, a = 1 },
            body = "Xin chào",
        });

        Assert.AreEqual("1ko5mmj", WebV6Backup.ContentHash(first));
        Assert.AreEqual("1bvo257", WebV6Backup.ContentHash(second));
    }

    [TestMethod]
    [DataRow(0.0000001, "1viiy56")]
    [DataRow(0.000001, "jstk77")]
    [DataRow(100000000000000000000d, "1o6ppuf")]
    [DataRow(1e21, "1noqbok")]
    [DataRow(0.00000123, "1wntvmq")]
    [DataRow(123456789.12345679, "be6itc")]
    public void ContentHash_MatchesWebNumberFormatting(double value, string expectedHash)
    {
        var content = JsonSerializer.SerializeToElement(new { value });

        Assert.AreEqual(expectedHash, WebV6Backup.ContentHash(content));
    }

    [TestMethod]
    public void CreateAndParse_RoundTripsV2Backup()
    {
        var expected = NoteLibraryTestData.CreateLinkedWeb();
        var backup = WebV6Backup.Create(expected, exportedAt: 1234);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(backup, JsonDefaults.Create());

        var actual = WebV6Backup.Parse(bytes);

        WebV6Backup.VerifyRoundTrip(expected, actual);
        Assert.AreEqual(WebV6Schema.SheetHashAlgorithm, backup.SheetHashAlgorithm);
        Assert.AreEqual(2, backup.SheetContentHashes.Count);
    }

    [TestMethod]
    public void Parse_RejectsMarkedBackupWithTamperedSheet()
    {
        var backup = WebV6Backup.Create(NoteLibraryTestData.CreateWeb(1));
        backup.Library.SheetContents["sheet-1"] = JsonSerializer.SerializeToElement(new { body = "tampered" });
        var bytes = JsonSerializer.SerializeToUtf8Bytes(backup, JsonDefaults.Create());

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => WebV6Backup.Parse(bytes));

        StringAssert.Contains(exception.Message, "Hash nội dung Sheet");
    }

    [TestMethod]
    public void Parse_AllowsStructurallyCompleteLegacyV2HashOnce()
    {
        var backup = WebV6Backup.Create(NoteLibraryTestData.CreateWeb(1), exportedAt: 1234) with
        {
            SheetHashAlgorithm = null,
            SheetContentHashes = new Dictionary<string, string> { ["sheet-1"] = "legacy-pre-json-hash" },
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(backup, JsonDefaults.Create());

        var restored = WebV6Backup.Parse(bytes);

        Assert.AreEqual("Content for sheet-1", restored.SheetContents["sheet-1"].GetProperty("body").GetString());
    }

    [TestMethod]
    public void Parse_RejectsLegacyBackupWithIncompleteHashSet()
    {
        var backup = WebV6Backup.Create(NoteLibraryTestData.CreateWeb(1), exportedAt: 1234) with
        {
            SheetHashAlgorithm = null,
            SheetContentHashes = [],
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(backup, JsonDefaults.Create());

        Assert.ThrowsExactly<InvalidDataException>(() => WebV6Backup.Parse(bytes));
    }

    [TestMethod]
    public void StableStringify_IsIndependentOfObjectPropertyOrder()
    {
        using var left = JsonDocument.Parse("{\"z\":2,\"a\":{\"y\":true,\"x\":1}}");
        using var right = JsonDocument.Parse("{\"a\":{\"x\":1,\"y\":true},\"z\":2}");

        Assert.AreEqual(WebV6Backup.StableStringify(left.RootElement), WebV6Backup.StableStringify(right.RootElement));
        Assert.AreEqual(WebV6Backup.ContentHash(left.RootElement), WebV6Backup.ContentHash(right.RootElement));
    }
}
