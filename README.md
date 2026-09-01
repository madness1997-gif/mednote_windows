# MedNote Reader for Windows

MedNote Reader for Windows is a clean-room, Windows-native rewrite of the Reader half of the existing `mednote-reader` web application. It reuses the stable data contracts and proven navigation rules, but it does **not** embed the React application, Chromium, Electron, or WebView.

## Current preview

The first vertical slice already contains:

- WinUI 3 shell on .NET 10 and Windows App SDK 2.4;
- native PDF rendering through PDFiumCore/PDFium;
- open and drag/drop PDF;
- single-page and virtualized continuous modes;
- a zero-encode PDFium BGRA → Win2D/Direct2D display path;
- page navigation, fit-page, fit-width, and zoom;
- hand/pan as the default Reader tool;
- bookmarks;
- exact document IDs compatible with the web app's FNV-1a identity;
- page/within-page scroll anchors persisted under `%LOCALAPPDATA%\MedNote Reader`;
- a 192 MB CPU/GPU surface budget, one-slot render scheduler, and cancellation
  when pages leave the viewport;
- one-pass page-metrics discovery and a packaged-app smoke benchmark that jumps
  directly to page 1,500 of a generated 3,000-page mixed-size PDF;
- opaque preservation of existing web PDF annotations during JSON round-trips.

The M2 backend now implements renderer-independent PDF outline destinations,
text extraction rectangles, cancellable search, and a bounded 32 MB LRU text
cache through PDFium. Every native call is serialized on an owned dispatcher;
PDFium handles never cross the adapter boundary.

## Architecture

```text
MedNote.Windows.App
  WinUI views, native PDF adapter, local JSON persistence
            │
            ▼
MedNote.Core
  web-compatible contracts, navigation, anchors, virtualization, memory budget
```

`MedNote.Core` has no Windows UI dependency. PDFium stays behind
`IPdfEngine`/`IPdfDocumentSession`, so navigation, persistence, and view-model
contracts do not depend on native handle types.

Read [architecture.md](docs/architecture.md), [web-compatibility.md](docs/web-compatibility.md), and [roadmap.md](docs/roadmap.md) before expanding the app.
The PDFium selection and threading boundary are recorded in
[ADR 0001](docs/decisions/0001-pdfium-backend.md).

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

Text selection, annotations editing, Note, Google Drive sync, and an installer
are deliberately not coupled to the renderer. Search and outline extraction
exist at the backend contract level; their full Reader UI remains roadmap work.
