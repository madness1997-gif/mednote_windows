# Native rewrite roadmap

## M1 — Reader vertical slice

- [x] separate repository/solution;
- [x] WinUI 3 shell without WebView;
- [x] open and drag/drop PDF;
- [x] single and virtualized continuous modes;
- [x] zoom, fit, page navigation, pan, bookmarks;
- [x] anchor-based position restore;
- [x] local atomic persistence;
- [x] web-compatible document IDs and Reader JSON;
- [x] 3,000-page virtualization and bitmap-budget tests.
- [x] Direct2D page surfaces without PNG encode/decode;
- [x] bounded/cancellable render scheduler and cached page metrics;
- [x] packaged 3,000-page page-jump smoke benchmark with a memory ceiling;
- [x] DPI-aware layout refresh and ratio-based two-pass position restore.
- [x] recover realized Direct2D surfaces after device/surface loss.

## M2 — Production PDFium backend

- [x] select PDFiumCore raw bindings and document the native boundary;
- [x] add renderer-independent outline/text/destination contracts;
- [x] add cancellable page search with a bounded 32 MB LRU text cache;
- [x] replace the milestone renderer behind `IPdfEngine`;
- [x] outline and destination resolution;
- [x] text layer, selection, copy, and English–Vietnamese dictionary action;
- [x] connect PDFium text extraction and result rectangles;
- [x] incremental indexing;
- [x] thumbnail virtualization;
- [x] rotation-aware intrinsic PDF and persisted Reader render/layout;
- [x] encrypted/password PDF prompt and non-persistent password handoff;
- [x] generated mixed-page-size 3,000-page CI fixture;
- [x] generated adversarial CI corpus: tables, scan-like images, invalid page
  boxes, extreme ratios, and encrypted PDF;

## P2.5 — Reader stabilization before feature growth

- [x] keep `MainWindow` as composition/lifecycle and split command/sidebar routing;
- [x] move Reader chrome projection, sidebar state and search debounce to controllers;
- [x] move progressive search concurrency out of `ReaderViewModel`;
- [x] split PDFium engine/session/render/text/outline responsibilities without changing native handle ownership;
- [x] define a local-only real-world PDF corpus policy;
- [x] add an opt-in manual memory soak harness that is not part of every push.

See [p2.5-stabilization.md](p2.5-stabilization.md).

## M3 — Reader annotations

- [x] highlight, underline, strikeout, squiggly, area highlight;
- [x] pen/eraser and shape objects;
- [x] undo/redo sessions;
- [x] crop result contract for Note;
- [x] web annotation JSON parity and export flattening.

## M4 — Native Note foundation

- [x] native RTF repository, staged replacement and isolated web-v6 import contract;
- [x] shared library hierarchy and Document–Page/Sheet links;
- [ ] Reader/Note split layout and F6 switching;
- [ ] RichEdit-based native editing and First Aid blocks;
- [ ] source anchors from Note back to PDF;
- [ ] exact Reader position preservation across pane changes.

See [m4.1-data-foundation.md](m4.1-data-foundation.md).

## M5 — Sync and distribution

- [ ] Desktop OAuth with PKCE/loopback and `drive.appdata`;
- [ ] conflict-safe v2 manifest sync;
- [ ] Windows installer, safe uninstall, and shutdown lifecycle;
- [ ] signed release channel, updater, crash diagnostics;
- [ ] measure working set/startup against the Electron baseline.

Each milestone must ship from this repository independently. The web repository remains production-safe until native parity is demonstrated with fixtures and real-use tests.
