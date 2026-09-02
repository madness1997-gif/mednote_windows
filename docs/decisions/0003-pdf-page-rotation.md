# ADR 0003: Keep PDF and Reader rotation in one render geometry

## Status

Accepted.

## Context

PDF pages can carry an intrinsic clockwise `/Rotate` value, while the MedNote
Reader state also preserves a document-level rotation compatible with the web
v6 payload. Applying only a XAML transform would make the visible bitmap diverge
from its layout, raster budget, and future page-coordinate hit testing. Reusing
a cached bitmap after a 180-degree change is also incorrect even though its
dimensions do not change.

## Decision

- Treat `FPDF_GetPageSizeByIndexF` and loaded-page width/height as the display
  geometry after PDFium has applied the page's intrinsic rotation.
- Normalize Reader rotation to 0/90/180/270 degrees and swap the layout aspect
  ratio for odd quarter turns.
- Pass the Reader rotation as PDFium's clockwise quarter-turn render argument;
  do not rotate the resulting Direct2D surface in XAML.
- Include rotation in page-surface cache validity and reject an in-flight
  result if the requested rotation changed before completion.
- Keep PDF text and annotation data in PDF page coordinates. Future device/page
  coordinate conversion must use the same viewport size and rotation that were
  used to render the page.

## Consequences

- Intrinsic and user rotation compose inside PDFium, while XAML always receives
  a correctly oriented bitmap with matching dimensions.
- A rotation change cancels stale raster work and rerenders realized pages;
  unmaterialized pages retain only cheap geometry updates.
- The CI rotation corpus uses distinct colored corner markers, so it detects
  incorrect content orientation as well as incorrect portrait/landscape size.

## References

- [PDFium `FPDF_RenderPageBitmap` contract](https://pdfium.googlesource.com/pdfium/+/refs/heads/main/public/fpdfview.h)
- [PDFium intrinsic page-dimension rotation](https://pdfium.googlesource.com/pdfium/+/refs/heads/main/core/fpdfapi/page/cpdf_page.cpp)
