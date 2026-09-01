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
- Implements `IPdfEngine` with `Windows.Data.Pdf` for milestone 1.
- Virtualizes page controls with `ListView`/`ItemsStackPanel`.
- Converts native rendered PNG streams into `BitmapImage` only for realized pages.
- Persists state atomically under `%LOCALAPPDATA%\MedNote Reader`.

## Renderer strategy

`Windows.Data.Pdf` gives the first milestone a small, dependency-light renderer and validates the native shell. It does not expose all PDF features needed by MedNote, especially outline extraction, text geometry, search, and annotation authoring. The next renderer is therefore PDFium behind the same interface; UI and domain code must not import PDFium types.

M2 adds optional capabilities instead of enlarging the base rendering contract:

- `IPdfOutlineProvider` returns normalized outline nodes and zero-based destinations;
- `IPdfTextProvider` returns page text and rectangles for a character range;
- `PdfTextSearchService` scans providers with cancellation and a bounded LRU cache;
- UI code detects capabilities on the active session and remains usable when a
  renderer only supports page images.

The PDFium adapter must route every native call through one owned dispatcher.
Page rendering may be requested concurrently by the UI, but PDFium handle access
is serialized before reaching the native library. No PDFium handle crosses the
adapter boundary. See `docs/decisions/0001-pdfium-backend.md`.

## Performance invariants

1. Opening a file must not copy the complete PDF into managed memory.
2. Only realized/nearby pages may render.
3. A page leaving the realized viewport cancels unfinished rendering.
4. Decoded bitmap estimates are capped by a 192 MB LRU budget; visible pages are pinned.
5. Extracted text is capped by a separate 32 MB LRU budget.
6. A 3,000-page document may allocate page metadata, but not 3,000 bitmaps or controls.
7. Restore uses `(page, within-page ratio, horizontal offset)`, never only an absolute scroll pixel.
8. Persistence is atomic and does not block Windows shutdown on a long synchronization path.
