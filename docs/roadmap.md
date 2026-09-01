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

## M2 — Production PDFium backend

- [x] select PDFiumCore raw bindings and document the native boundary;
- [x] add renderer-independent outline/text/destination contracts;
- [x] add cancellable page search with a bounded 32 MB LRU text cache;
- [x] replace the milestone renderer behind `IPdfEngine`;
- [x] outline and destination resolution;
- [ ] text layer, selection, copy, and English–Vietnamese dictionary action;
- [x] connect PDFium text extraction and result rectangles;
- [ ] incremental indexing;
- [ ] thumbnail virtualization;
- [ ] rotation-aware render/layout;
- [ ] encrypted/password PDF flow;
- [ ] test corpus: scans, tables, mixed page sizes, 2,000–3,000 pages.

## M3 — Reader annotations

- [ ] highlight, underline, strikeout, squiggly, area highlight;
- [ ] pen/eraser and shape objects;
- [ ] undo/redo sessions;
- [ ] crop result contract for Note;
- [ ] web annotation JSON parity and export flattening.

## M4 — Native Note foundation

- [ ] v6 repository and staged backup import;
- [ ] library hierarchy and Document–Page/Sheet links;
- [ ] Reader/Note split layout and F6 switching;
- [ ] native rich text and First Aid blocks;
- [ ] source anchors from Note back to PDF;
- [ ] exact Reader position preservation across pane changes.

## M5 — Sync and distribution

- [ ] Desktop OAuth with PKCE/loopback and `drive.appdata`;
- [ ] conflict-safe v2 manifest sync;
- [ ] Windows installer, safe uninstall, and shutdown lifecycle;
- [ ] signed release channel, updater, crash diagnostics;
- [ ] measure working set/startup against the Electron baseline.

Each milestone must ship from this repository independently. The web repository remains production-safe until native parity is demonstrated with fixtures and real-use tests.
