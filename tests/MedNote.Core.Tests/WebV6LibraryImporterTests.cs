using System.Text.Json;
using MedNote.Core;
using MedNote.Core.Compatibility.WebV6;
using MedNote.Infrastructure;
using MedNote.Infrastructure.Compatibility.WebV6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class WebV6LibraryImporterTests
{
    [TestMethod]
    public async Task Import_ConvertsEverySheetBeforeAtomicNativeReplacement()
    {
        using var directory = new TemporaryRepositoryDirectory();
        await using var repository = new FileNoteRepository(directory.Path);
        var current = NoteLibraryTestData.Create(1);
        await repository.ReplaceLibraryAsync(current);
        var web = NoteLibraryTestData.CreateWeb(3);
        var backup = WebV6Backup.Create(web, exportedAt: 1234);
        await using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(backup, JsonDefaults.Create()));
        var converter = new TestWebSheetConverter();

        var result = await new WebV6LibraryImporter(repository).ImportAsync(stream, converter);
        var imported = await repository.LoadLibraryAsync();

        Assert.IsNotNull(imported);
        Assert.AreEqual(3, converter.ConversionCount);
        Assert.AreEqual(3, result.SheetCount);
        Assert.AreEqual(3, result.WebSheetContentHashes.Count);
        Assert.AreEqual("Content for sheet-2", converter.ConvertedBodies["sheet-2"]);
        Assert.IsTrue(imported.SheetContents["sheet-2"].Rtf.Contains("Content for sheet-2", StringComparison.Ordinal));
        NoteLibraryValidator.AssertValid(imported);
    }

    [TestMethod]
    public async Task Import_TamperedWebBackupLeavesCurrentNativeLibraryUntouched()
    {
        using var directory = new TemporaryRepositoryDirectory();
        await using var repository = new FileNoteRepository(directory.Path);
        var current = NoteLibraryTestData.Create(1);
        await repository.ReplaceLibraryAsync(current);
        var backup = WebV6Backup.Create(NoteLibraryTestData.CreateWeb(1));
        backup.Library.SheetContents["sheet-1"] = JsonSerializer.SerializeToElement(new { body = "tampered" });
        await using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(backup, JsonDefaults.Create()));
        var converter = new TestWebSheetConverter();

        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
        {
            _ = await new WebV6LibraryImporter(repository).ImportAsync(stream, converter);
        });
        var reloaded = await repository.LoadLibraryAsync();

        Assert.IsNotNull(reloaded);
        Assert.AreEqual(0, converter.ConversionCount);
        NativeLibrarySnapshotVerifier.AssertEquivalent(current, reloaded);
    }

    [TestMethod]
    public async Task Import_ConverterFailureLeavesCurrentNativeLibraryUntouched()
    {
        using var directory = new TemporaryRepositoryDirectory();
        await using var repository = new FileNoteRepository(directory.Path);
        var current = NoteLibraryTestData.Create(1);
        await repository.ReplaceLibraryAsync(current);
        var backup = WebV6Backup.Create(NoteLibraryTestData.CreateWeb(2));
        await using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(backup, JsonDefaults.Create()));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
        {
            _ = await new WebV6LibraryImporter(repository).ImportAsync(stream, new FailingWebSheetConverter());
        });
        var reloaded = await repository.LoadLibraryAsync();

        Assert.IsNotNull(reloaded);
        NativeLibrarySnapshotVerifier.AssertEquivalent(current, reloaded);
    }

    private sealed class TestWebSheetConverter : IWebV6SheetContentConverter
    {
        public int ConversionCount { get; private set; }

        public Dictionary<string, string> ConvertedBodies { get; } = [];

        public ValueTask<RtfSheetContent> ConvertAsync(
            string sheetId,
            JsonElement webContent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = webContent.GetProperty("body").GetString() ?? string.Empty;
            ConversionCount++;
            ConvertedBodies.Add(sheetId, body);
            return ValueTask.FromResult(new RtfSheetContent
            {
                Rtf = $@"{{\rtf1\ansi\pard {EscapeRtf(body)}\par}}",
            });
        }

        private static string EscapeRtf(string value) => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("{", "\\{", StringComparison.Ordinal)
            .Replace("}", "\\}", StringComparison.Ordinal);
    }

    private sealed class FailingWebSheetConverter : IWebV6SheetContentConverter
    {
        public ValueTask<RtfSheetContent> ConvertAsync(
            string sheetId,
            JsonElement webContent,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException($"Không chuyển được {sheetId}.");
    }
}
