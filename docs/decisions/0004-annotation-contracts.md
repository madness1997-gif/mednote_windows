# ADR 0004 — Reader annotation coordinates and export

## Decision

Use the web v6 `PdfAnnotation` discriminated union as the persisted contract.
Annotation rectangles and points remain in bottom-origin PDF page coordinates;
zoom, fit mode, DPI and the Reader's additional rotation are presentation-only.

Known annotation kinds are exposed as typed core records. Unknown kinds and
unknown fields are retained as JSON so opening and editing a document in the
native Reader cannot destroy data written by a newer web client.

Undo/redo is a transient, per-document session capped at 60 snapshots. Every
committed snapshot is still queued through the existing atomic Reader store.

## Rendering and crop

WinUI draws the annotation overlay only for realized page presenters. Pen
samples, markup rectangles and objects are mapped from PDF coordinates when a
page is displayed, so changing layout does not rewrite annotation data.

Crop requests carry a page index, PDF rectangle and Reader rotation. PDFium
re-renders just that clipped region at up to 4096 pixels on its longest edge;
the result contract contains lossless PNG bytes ready for the M4 Note layer.

## Flattened export

Export opens an independent PDFium document, appends annotation paths/text to
the affected page content streams, calls `FPDFPage_GenerateContent`, and saves
atomically to a separate path. This preserves the source PDF's vector content,
searchable text, bookmarks and existing embedded annotations. The live Reader
session and source file are never mutated.

Passwords may be retained only in the active native session and are passed to
the independent export open call. They are never written to Reader JSON.
