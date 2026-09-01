# ADR 0001: Use PDFiumCore behind an owned adapter

- Status: accepted for M2
- Date: 2026-09-01

## Context

The M1 `Windows.Data.Pdf` adapter renders pages reliably but does not expose the
document outline, text geometry, native search, link destinations, or password
handling required by the Reader roadmap. The replacement must remain compatible
with unpackaged WinUI 3 on x64 Windows and must not introduce Chromium, WebView,
Electron, or a second UI framework.

## Decision

Use the `PDFiumCore` NuGet package as generated low-level P/Invoke bindings and
native runtime assets. Keep all lifecycle, threading, validation, and conversion
code inside a new `PdfiumPdfEngine` adapter owned by this repository.

The adapter will:

1. initialize and destroy the PDFium library once per process;
2. serialize every PDFium call through a dedicated dispatcher because document,
   page, text-page, bitmap, bookmark, and destination handles are not shared with
   callers;
3. copy render results into managed PNG/bitmap data before returning;
4. translate bookmarks, page destinations, extracted text, and rectangles into
   `MedNote.Core` contracts;
5. bound malformed outline traversal by both a visited-handle set and a maximum
   node count;
6. close native handles deterministically in reverse ownership order;
7. remove `WindowsPdfEngine` once the packaged PDFium smoke test opens and
   renders the fixture on Windows CI.

Package versions remain centrally pinned in `Directory.Packages.props`. The
adapter initially ships with PDFiumCore `153.0.7999`; restore and native asset
publication are tested in the same commit.

## Why this option

PDFiumCore tracks current PDFium-generated headers and includes win-x64 native
assets while exposing the underlying API surface needed by MedNote. A higher
level viewer package would couple rendering and UI concerns or hide outline/text
details that the native Reader needs to control.

The core contracts and search service do not reference PDFiumCore. Replacing the
binding later therefore affects only the adapter project.

## Consequences

- Native package provenance and PDFium license notices must ship with releases.
- PDFium upgrades require the PDF corpus and ABI smoke tests before merge.
- The serialized dispatcher limits native parallelism; the UI still remains
  responsive through async queues, cancellation, render prioritization, and the
  bitmap/text caches.
- Search currently performs a cancellable sequential scan. Incremental indexing
  is added only after real PDFium text extraction is measured on 2,000–3,000 page
  documents.

## Primary references

- PDFiumCore: https://github.com/Dtronix/PDFiumCore
- PDFium text API: https://pdfium.googlesource.com/pdfium/+/main/public/fpdf_text.h
- PDFium bookmark API: https://pdfium.googlesource.com/pdfium/+/main/public/fpdf_doc.h
