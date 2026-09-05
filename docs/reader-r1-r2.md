# Reader R1 / R2 — 0.5.3

Reference: web Reader at `ee0bb98555de32accbbd6718fc5199734bf1c122`.
Implementation stays native: PDFium, Direct2D and WinUI controls.

| Area | Native change |
| --- | --- |
| Toolbar | Outline icons, label on the active tool, two scrollable rows, fixed undo/redo |
| Display | Fit page/width, rotate, single/continuous and Reader-only in one flyout |
| Navigation | Narrow thumbnail rail, selected page, bounded previous/next, reopen rail |
| Selection | Smart text hit test; blank-area panning; Space temporarily pans |
| Selection menu | Copy, translate, Note, Oxford and four text markup actions |
| Dictionary | MyMemory English–Vietnamese; DictionaryAPI pronunciation and definitions; explicit request, cancellation on close, retry |
| Note | Selected text or translation inserted at the caret; content and PDF source saved atomically; rollback on save failure |
| Annotations | All/current-page list, source navigation, color indicator, undoable deletion, custom RGB/hex color |
| Search | Enter/Tìm, Ctrl+F, incremental results, result/page count, 500-result cap |
| Shortcuts | Space pan, Ctrl+wheel and Ctrl+plus/minus zoom with position restoration |
| Bookmarks | Remove directly from the list |

Search remains scoped to the open PDF. This change does not introduce a
cross-document search index or assert pixel-level parity with the web.

## Validation

The workflow runs core tests, a native Windows build, packaged Direct2D render
probes (including the 3,000-page corpus, rotation and surface recovery), and
installer launch/uninstall checks. Dictionary tests use mocked HTTP for useful
results, provider failure, phrase routing and cancellation. A native window
capture is attached separately for visual review when the runner supports it.

Desktop interaction review should cover Smart dragging over text and whitespace,
Space release, selection popup dismissal while lookup is pending, custom color,
annotation deletion/undo, zoom anchoring, text-to-Note followed by source return,
and narrow-window toolbar access. Build/render probes alone do not verify these
pointer/keyboard interactions.
