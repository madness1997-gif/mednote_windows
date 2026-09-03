using System.Runtime.InteropServices;
using System.Text;
using MedNote.Core;
using PDFiumCore;

namespace MedNote.Windows.App.Infrastructure;

internal static class PdfiumOutlineReader
{
    private const int MaximumOutlineDepth = 64;
    private const int MaximumOutlineNodes = 10_000;
    private const ulong MaximumOutlineTitleBytes = 1_048_576;

    public static IReadOnlyList<PdfOutlineNode> Read(FpdfDocumentT document, int pageCount)
    {
        var visited = new HashSet<IntPtr>();
        var nodeCount = 0;
        return ReadLevel(document, pageCount, null, 0, visited, ref nodeCount);
    }

    private static IReadOnlyList<PdfOutlineNode> ReadLevel(
        FpdfDocumentT document,
        int pageCount,
        FpdfBookmarkT? parent,
        int depth,
        HashSet<IntPtr> visited,
        ref int nodeCount)
    {
        if (depth >= MaximumOutlineDepth || nodeCount >= MaximumOutlineNodes)
        {
            return Array.Empty<PdfOutlineNode>();
        }

        var nodes = new List<PdfOutlineNode>();
        var bookmark = fpdf_doc.FPDFBookmarkGetFirstChild(document, parent!);
        while (bookmark is not null
            && nodeCount < MaximumOutlineNodes
            && visited.Add(bookmark.__Instance))
        {
            nodeCount++;
            var children = ReadLevel(document, pageCount, bookmark, depth + 1, visited, ref nodeCount);
            nodes.Add(new PdfOutlineNode(
                ReadBookmarkTitle(bookmark),
                ResolveDestination(document, pageCount, bookmark),
                children,
                fpdf_doc.FPDFBookmarkGetCount(bookmark) > 0));
            bookmark = fpdf_doc.FPDFBookmarkGetNextSibling(document, bookmark);
        }

        return nodes;
    }

    private static string ReadBookmarkTitle(FpdfBookmarkT bookmark)
    {
        var requiredBytes = fpdf_doc.FPDFBookmarkGetTitle(bookmark, IntPtr.Zero, 0);
        if (requiredBytes < 2 || requiredBytes > MaximumOutlineTitleBytes)
        {
            return string.Empty;
        }

        var buffer = Marshal.AllocHGlobal(checked((int)requiredBytes));
        try
        {
            var writtenBytes = Math.Min(
                requiredBytes,
                fpdf_doc.FPDFBookmarkGetTitle(bookmark, buffer, requiredBytes));
            var bytes = new byte[checked((int)writtenBytes)];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            var textLength = bytes.Length;
            while (textLength >= 2 && bytes[textLength - 1] == 0 && bytes[textLength - 2] == 0)
            {
                textLength -= 2;
            }

            return Encoding.Unicode.GetString(bytes, 0, textLength);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static PdfDestination? ResolveDestination(
        FpdfDocumentT document,
        int pageCount,
        FpdfBookmarkT bookmark)
    {
        var destination = fpdf_doc.FPDFBookmarkGetDest(document, bookmark);
        if (destination is null)
        {
            var action = fpdf_doc.FPDFBookmarkGetAction(bookmark);
            if (action is not null && fpdf_doc.FPDFActionGetType(action) == 1)
            {
                destination = fpdf_doc.FPDFActionGetDest(document, action);
            }
        }

        if (destination is null)
        {
            return null;
        }

        var pageIndex = fpdf_doc.FPDFDestGetDestPageIndex(document, destination);
        if (pageIndex < 0 || pageIndex >= pageCount)
        {
            return null;
        }

        var hasX = 0;
        var hasY = 0;
        var hasZoom = 0;
        var x = 0f;
        var y = 0f;
        var zoom = 0f;
        var hasLocation = fpdf_doc.FPDFDestGetLocationInPage(
            destination,
            ref hasX,
            ref hasY,
            ref hasZoom,
            ref x,
            ref y,
            ref zoom) != 0;
        return new PdfDestination(
            pageIndex,
            hasLocation && hasX != 0 ? x : null,
            hasLocation && hasY != 0 ? y : null,
            hasLocation && hasZoom != 0 && zoom > 0 ? zoom : null);
    }
}
