# MedNote Reader for Windows

MedNote Reader for Windows is a clean-room, Windows-native rewrite of the existing
`mednote-reader` web application. It reuses stable data contracts and proven
navigation rules, but it does **not** embed React, Chromium, Electron, or a
WebView.

## Current preview

The first vertical slice already contains:

- WinUI 3 shell on .NET 10 and Windows App SDK 2.4;
- native PDF rendering through PDFiumCore/PDFium;
- open and drag/drop PDF;
- single-page and virtualized continuous modes;
- a zero-encode PDFium BGRA → Win2D/Direct2D display path;
- automatic Direct2D surface recreation after GPU device loss or XAML surface loss;
- page navigation, fit-page, fit-width, and zoom;
- hand/pan as the default Reader tool;
- bookmarks;
- exact document IDs compatible with the web app's FNV-1a identity;
- page/within-page scroll anchors persisted under `%LOCALAPPDATA%\MedNote Reader`;
- a 192 MB CPU/GPU surface budget, one-slot render scheduler, and cancellation
  when pages leave the viewport;
- one-pass page-metrics discovery and a packaged-app smoke benchmark that jumps
  directly to page 1,500 of a generated 3,000-page mixed-size PDF;
- password-protected PDF opening without persisting the password;
- intrinsic PDF page rotation plus persisted Reader rotation through the
  PDFium render and Direct2D layout path;
- native PDFium text hit-testing with rotation-aware selection highlights,
  clipboard copy, and an explicit English–Vietnamese lookup action;
- cancellable, page-by-page search that streams results while a bounded text
  cache incrementally indexes the document;
- virtualized sidebar thumbnails that render only for realized list items and
  share the existing bitmap budget;
- packaged adversarial PDF gates for vector tables, scan-like images, broken
  page boxes, extreme aspect ratios, and encrypted documents;
- opaque preservation of unknown/future web PDF annotations during JSON round-trips;
- native highlight/underline/strikeout/squiggly and arbitrary-area markup;
- pressure-sampled pen, whole-object eraser, rectangle/ellipse/arrow tools, and
  per-document 60-step undo/redo;
- PDF-coordinate annotation overlays that remain anchored through zoom, fit,
  continuous/single layout, DPI and Reader rotation changes;
- high-resolution PDFium crop results ready for the future Note pane;
- atomic PDFium export that flattens web-compatible annotations into page
  content without rasterizing the source document;
- shared Note hierarchy/Document-link contracts with native RTF Sheet content;
- lazy, SHA-256-addressed RTF storage behind an atomic manifest;
- isolated web-v6 backup validation and a web→RTF conversion boundary that
  finishes before native staged replacement begins;
- one native Library manifest shared by Reader and Note after a non-destructive
  Reader-v1 migration;
- Reader, Note and adjustable split modes with persisted 20–80% sizing and F6
  pane switching that preserves PDF position and Note caret;
- a lazy active-Sheet RichEdit editor with native RTF autosave, basic text/list
  formatting and a 12-point First Aid table whose content cells start empty;
- a concrete one-way web-v6 readable-text converter that emits native RTF.

The M3 Reader now implements renderer-independent PDF outline destinations,
text extraction rectangles, cancellable search, and a bounded 32 MB LRU text
cache through PDFium. Every native call is serialized on an owned dispatcher;
PDFium handles never cross the adapter boundary.

## Architecture

```text
MedNote.Windows.App
  WinUI views and native PDF adapter
            │
            ▼
MedNote.Infrastructure
  atomic native manifest and lazy RTF Sheet blobs
            │
            ▼
MedNote.Core
  shared metadata, native RTF, web adapters, navigation and memory contracts
```

`MedNote.Core` and `MedNote.Infrastructure` have no Windows UI dependency. PDFium stays behind
`IPdfEngine`/`IPdfDocumentSession`, so navigation, persistence, and view-model
contracts do not depend on native handle types.

Read [architecture.md](docs/architecture.md),
[m4.2-native-workspace.md](docs/m4.2-native-workspace.md),
[web-compatibility.md](docs/web-compatibility.md), and
[roadmap.md](docs/roadmap.md) before expanding the app.
The PDFium selection and threading boundary are recorded in
[ADR 0001](docs/decisions/0001-pdfium-backend.md).
The Direct2D resource lifecycle is recorded in
[ADR 0002](docs/decisions/0002-direct2d-surface-lifecycle.md).
Page rotation ownership and rendering are recorded in
[ADR 0003](docs/decisions/0003-pdf-page-rotation.md).
Annotation persistence, coordinates, crop and export are recorded in
[ADR 0004](docs/decisions/0004-annotation-contracts.md).

## Build

Requirements:

- Windows 10 version 1809 or later;
- Visual Studio 2026 with the WinUI application development workload, or .NET 10 SDK;
- x64 architecture for the first milestone.

```powershell
dotnet restore MedNote.Windows.sln -p:Platform=x64
dotnet test tests/MedNote.Core.Tests/MedNote.Core.Tests.csproj -c Release
dotnet build src/MedNote.Windows.App/MedNote.Windows.App.csproj -c Release -p:Platform=x64
```

To produce the self-contained folder distributed by CI:

```powershell
dotnet publish src/MedNote.Windows.App/MedNote.Windows.App.csproj `
  -c Release -r win-x64 --self-contained true -p:Platform=x64 `
  -p:WindowsAppSDKSelfContained=true -o artifacts/MedNote-Reader-Windows
```

## Non-goals for this milestone

M4.2 does not yet implement source anchors, crop/image handoff into Note,
advanced block/canvas editing, reverse RTF→web conversion, Google Drive sync,
or an installer. Those stay in M4.3 or M5.
