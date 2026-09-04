using System.Text.Json;
using MedNote.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MedNote.Core.Tests;

[TestClass]
public sealed class PdfAnnotationTests
{
    [TestMethod]
    public void WebJson_RoundTripsAllKnownFieldsAndExtensionData()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "id":"pdf-highlight-1",
              "kind":"area-highlight",
              "page":3,
              "color":"#F6D96B",
              "rects":[{"x1":10,"y1":20,"x2":40,"y2":60}],
              "text":"",
              "createdAt":1700000000000,
              "futureField":{"kept":true}
            }
            """);

        Assert.IsTrue(PdfAnnotationJson.TryDeserialize(document.RootElement, out var annotation));
        Assert.IsNotNull(annotation);
        Assert.AreEqual(PdfAnnotationKind.AreaHighlight, annotation!.Kind);
        Assert.AreEqual(3, annotation.Page);
        Assert.AreEqual(1, annotation.Rects!.Count);

        var roundTrip = PdfAnnotationJson.Serialize(annotation);
        Assert.AreEqual("area-highlight", roundTrip.GetProperty("kind").GetString());
        Assert.IsTrue(roundTrip.GetProperty("futureField").GetProperty("kept").GetBoolean());
        Assert.AreEqual(10d, roundTrip.GetProperty("rects")[0].GetProperty("x1").GetDouble());
        Assert.AreEqual(4, roundTrip.GetProperty("rects")[0].EnumerateObject().Count());
    }

    [TestMethod]
    public void UnknownKind_RemainsOpaqueWhileNativeAnnotationsAreEdited()
    {
        using var document = JsonDocument.Parse(
            """
            [
              {"id":"future-1","kind":"future-cloud","page":2,"payload":[1,2,3]},
              {"id":"ink-1","kind":"ink","page":1,"color":"#111111","width":2,"points":[{"x":1,"y":2,"pressure":0.5}],"createdAt":12}
            ]
            """);
        var session = new PdfAnnotationSession();
        session.Reset(document.RootElement.EnumerateArray());

        Assert.AreEqual(1, session.Annotations.Count);
        Assert.IsTrue(session.Add(NewRectangle("rect-1", 2)));
        Assert.IsTrue(session.Delete("ink-1"));

        var snapshot = session.SnapshotJson();
        Assert.AreEqual(2, snapshot.Count);
        Assert.IsTrue(snapshot.Any(item => item.GetProperty("id").GetString() == "future-1"));
        Assert.AreEqual(
            3,
            snapshot.Single(item => item.GetProperty("id").GetString() == "future-1")
                .GetProperty("payload").GetArrayLength());
    }

    [TestMethod]
    public void Session_UndoRedoIsBoundedAndClearsRedoOnCommit()
    {
        var session = new PdfAnnotationSession(historyLimit: 2);
        session.Reset([]);
        session.Add(NewRectangle("one", 1));
        session.Add(NewRectangle("two", 1));
        session.Add(NewRectangle("three", 1));

        Assert.IsTrue(session.Undo());
        Assert.AreEqual(2, session.Annotations.Count);
        Assert.IsTrue(session.Undo());
        Assert.AreEqual(1, session.Annotations.Count);
        Assert.IsFalse(session.Undo());

        Assert.IsTrue(session.Redo());
        Assert.IsTrue(session.Add(NewRectangle("replacement", 1)));
        Assert.IsFalse(session.CanRedo);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(90)]
    [DataRow(180)]
    [DataRow(270)]
    public void AnnotationCoordinates_RoundTripDisplayPoint(int rotation)
    {
        var page = new PdfPageMetrics(600d, 800d);
        var displayWidth = rotation is 90 or 270 ? 800d : 600d;
        var displayHeight = rotation is 90 or 270 ? 600d : 800d;
        var source = new PdfPagePoint(displayWidth * 0.23d, displayHeight * 0.61d);

        var annotation = PdfAnnotationCoordinateMapper.DisplayToAnnotation(
            source,
            page,
            displayWidth,
            displayHeight,
            rotation);
        var result = PdfAnnotationCoordinateMapper.AnnotationToDisplay(
            annotation,
            page,
            displayWidth,
            displayHeight,
            rotation);

        Assert.AreEqual(source.X, result.X, 0.001d);
        Assert.AreEqual(source.Y, result.Y, 0.001d);
    }

    private static PdfAnnotation NewRectangle(string id, int page) => new()
    {
        Id = id,
        Kind = PdfAnnotationKind.Rectangle,
        Page = page,
        Color = "#123456",
        Width = 2d,
        Rect = new PdfAnnotationRect(10d, 20d, 40d, 50d),
        Text = string.Empty,
        CreatedAt = 42,
    };
}
