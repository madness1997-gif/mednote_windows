# Architecture decision record

## Decision

Build a separate Windows-only codebase using C# and WinUI 3. Keep the existing web repository intact and treat it as a behavioral/data-contract reference, not as a runtime dependency.

## Why this boundary exists

The Electron build necessarily launches Chromium renderer, GPU, utility, and main processes. A native rewrite can share neither that process model nor the DOM/CSS rendering layer. Reusing React components would reintroduce a WebView and would defeat the main reason for the rewrite.

The reusable assets from the web application are therefore conceptual and contractual:

| Web source | Native destination |
|---|---|
| `stable-id.ts` | `DocumentIdentity.cs` |
| `ReaderState` | `ReaderState.cs` |
| `pdf-page-virtualizer.ts` | `PageVirtualizer.cs` |
| `pdf-canvas-budget.ts` | `BitmapBudget.cs` |
| continuous scroll anchor logic | `ReaderPosition` plus `MainWindow` restoration |
| IndexedDB runtime state | atomic JSON metadata plus content-addressed RTF blobs |
| PDF.js/PDFium loaders | `IPdfEngine` adapter boundary |

No React, TypeScript, CSS, Vite, Electron, IndexedDB, or browser global is referenced by the native solution.

## Layers

### MedNote.Core

- Has no WinUI or Windows SDK dependency.
- Owns persisted contracts and invariants.
- Owns native RTF Sheet content; web v6 Note content exists only in a
  compatibility namespace and converter contract.
- Owns deterministic document identity.
- Owns pure navigation, virtualization, bitmap-budget, text-cache, and search algorithms.
- Owns web-compatible annotation records, edit history and coordinate mapping.
- Exposes PDF interfaces in terms of page metrics, rendered bytes, outline nodes, text, and rectangles.

### MedNote.Windows.App

- Owns WinUI views and user interaction.
- Owns the native RichEdit RTF boundary, Note toolbar and per-Sheet caret state.
- Owns Reader/Note visibility, split sizing and F6 focus routing without
  coupling those concerns to the PDF presenter.
- Implements `IPdfEngine`, `IPdfOutlineProvider`, `IPdfTextProvider`, and
  `IPdfTextHitTestProvider` with PDFiumCore/PDFium.
- Virtualizes page controls with `ListView`/`ItemsStackPanel`.
- Uploads tightly packed PDFium BGRA bytes into Win2D `CanvasBitmap`, then draws
  them onto a Direct2D-backed `CanvasImageSource` for realized pages. No PNG
  encode/decode step exists in the render path.
- Observes both Win2D `CanvasDevice.DeviceLost` and XAML
  `CompositionTarget.SurfaceContentsLost`; realized presenters rebuild their
  GPU surfaces from the budgeted managed BGRA buffer without rasterizing the
  PDF again.
- Treats PDFium page metrics as already containing the page dictionary's
  intrinsic `/Rotate`, then applies the normalized Reader rotation to layout,
  raster dimensions, cache validity, and `FPDF_RenderPageBitmap` together.
- Persists state atomically under `%LOCALAPPDATA%\MedNote Reader`.
- Draws annotation overlays only for realized pages and asks PDFium to flatten
  them into independent exported copies.

### MedNote.Infrastructure

- Implements the native v1 repository without WinUI or native PDF dependencies.
- Keeps hierarchy/document metadata in one atomically replaced manifest.
- Keeps each Sheet body in an immutable, content-addressed UTF-8 RTF blob so
  metadata-only startup and navigation never hydrate unrelated Sheets.
- Keeps web v6 parsing/conversion outside the repository and switches the live
  manifest only after a complete native snapshot reloads successfully.
- Projects the shared native Document graph through the existing Reader store
  contract, so Reader state and Note metadata use one live Library manifest.

## Renderer strategy

PDFium is the production renderer behind the core interfaces. It supplies page
images, outlines, destinations, text, and text rectangles without exposing
native types to UI or domain code. The milestone `Windows.Data.Pdf` adapter was
removed after the packaged PDFium render smoke test passed.

M2 adds optional capabilities instead of enlarging the base rendering contract:

- `IPdfOutlineProvider` returns normalized outline nodes and zero-based destinations;
- `IPdfTextProvider` returns page text and rectangles for a character range;
- `IPdfTextHitTestProvider` maps pointer locations to extracted-text indexes;
- `PdfTextSearchService` incrementally scans pages with cancellation, streamed
  results, and a bounded LRU cache;
- UI code detects capabilities on the active session and remains usable when a
  renderer only supports page images.

Reader selection geometry stays in page coordinates until presentation. The
UI applies the persisted Reader rotation only when mapping pointer locations or
drawing selection rectangles. Sidebar thumbnails use the same cancellable
render scheduler and bitmap budget as full pages, but their presenters pin work
only while `ListView` containers are realized.

The PDFium adapter routes every native call through one owned dispatcher. A
core render scheduler admits one page raster at a time and cancels queued work
as virtualized containers leave the viewport. PDFium handle access is therefore
serialized before reaching the native library, and no PDFium handle crosses the
adapter boundary. See `docs/decisions/0001-pdfium-backend.md`.

## Performance invariants

1. Opening a file must not copy the complete PDF into managed memory.
2. Only realized/nearby pages may render.
3. A page leaving the realized viewport cancels unfinished or queued rendering.
4. Managed BGRA plus Direct2D surface estimates are capped by a conservative
   192 MB LRU budget; visible pages are pinned.
5. Extracted text is capped by a separate 32 MB LRU budget.
6. A 3,000-page document may allocate page metadata, but not 3,000 bitmaps or controls.
7. Restore uses `(page, within-page ratio, horizontal offset)`, never only an absolute scroll pixel.
8. Persistence is atomic and does not block Windows shutdown on a long synchronization path.
9. CI must present page 1,500 from a generated 3,000-page mixed-size document
   through Direct2D within 45 seconds and below a 768 MiB process working set.
10. CI must force one surface-recreation cycle and render adversarial table,
    scan-like, invalid-box, extreme-ratio, and password-protected fixtures.
11. CI must verify portrait/landscape dimensions and four asymmetric corner
    markers for intrinsic and Reader rotations of 0/90/180/270 degrees.
12. Annotation geometry remains in PDF coordinates and must not be rewritten
    by zoom, viewport, DPI, fit-mode or Reader-rotation changes.
