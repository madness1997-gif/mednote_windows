using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MedNote.Core;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class JsonCompatibilityTests
{
    [TestMethod]
    public void ReaderState_UsesWebV6PropertyAndEnumNames()
    {
        var json = JsonSerializer.Serialize(
            new ReaderState
            {
                Page = 17,
                Zoom = 1.2,
                FitMode = PdfFitMode.Width,
                ViewMode = PdfViewMode.Continuous,
            },
            JsonDefaults.Create());

        StringAssert.Contains(json, "\"page\":17");
        StringAssert.Contains(json, "\"fitMode\":\"width\"");
        StringAssert.Contains(json, "\"viewMode\":\"continuous\"");
    }

    [TestMethod]
    public void ReaderState_PreservesOpaqueWebAnnotations()
    {
        const string json = """
            {
              "page": 3,
              "zoom": 1,
              "fitMode": "page",
              "rotation": 0,
              "viewMode": "single",
              "bookmarks": [],
              "annotations": [{"id":"ann-1","kind":"highlight","page":3,"color":"#f6d96b"}]
            }
            """;

        var state = JsonSerializer.Deserialize<ReaderState>(json, JsonDefaults.Create());
        var roundTrip = JsonSerializer.Serialize(state, JsonDefaults.Create());

        Assert.IsNotNull(state);
        Assert.AreEqual(1, state!.Annotations.Count);
        StringAssert.Contains(roundTrip, "\"ann-1\"");
        StringAssert.Contains(roundTrip, "\"highlight\"");
    }
}
