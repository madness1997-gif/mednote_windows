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
| IndexedDB runtime state | atomic JSON state in LocalAppData |
| PDF.js/PDFium loaders | `IPdfEngine` adapter boundary |

No React, TypeScript, CSS, Vite, Electron, IndexedDB, or browser global is referenced by the native solution.

## Layers

### MedNote.Core

- Has no WinUI or Windows SDK dependency.
- Owns persisted contracts and invariants.
- Owns deterministic document identity.
- Owns pure navigation, virtualization, bitmap-budget, text-cache, and search algorithms.
- Exposes PDF interfaces in terms of page metrics, rendered bytes, outline nodes, text, and rectangles.

### MedNote.Windows.App

- Owns WinUI views and user interaction.
- Implements `IPdfEngine`, `IPdfOutlineProvider`, and `IPdfTextProvider` with
  PDFiumCore/PDFium.
- Virtualizes page controls with `ListView`/`ItemsStackPanel`.
- Uploads tightly packed PDFium BGRA bytes into Win2D `CanvasBitmap`, then draws
  them onto a Direct2D-backed `CanvasImageSource` for realized pages. No PNG
  encode/decode step exists in the render path.
- Persists state atomically under `%LOCALAPPDATA%\MedNote Reader`.

## Renderer strategy

PDFium is the production renderer behind the core interfaces. It supplies page
images, outlines, destinations, text, and text rectangles without exposing
native types to UI or domain code. The milestone `Windows.Data.Pdf` adapter was
removed after the packaged PDFium render smoke test passed.

M2 adds optional capabilities instead of enlarging the base rendering contract:

- `IPdfOutlineProvider` returns normalized outline nodes and zero-based destinations;
- `IPdfTextProvider` returns page text and rectangles for a character range;
- `PdfTextSearchService` scans providers with cancellation and a bounded LRU cache;
- UI code detects capabilities on the active session and remains usable when a
  renderer only supports page images.

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
