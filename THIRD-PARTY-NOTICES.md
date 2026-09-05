# Third-party notices

MedNote Reader for Windows includes the following third-party components in its
x64 distribution.

## PDFiumCore

- Project: https://github.com/Dtronix/PDFiumCore
- Version: 153.0.7999
- License: Apache License 2.0

PDFiumCore provides the generated .NET P/Invoke bindings used by the Reader.

## PDFium native binaries

- Project: https://pdfium.googlesource.com/pdfium/
- Binary distribution: https://github.com/bblanchon/pdfium-binaries
- Version: Chromium/PDFium 7999, distributed by the `bblanchon.PDFium.Win32`
  dependency of PDFiumCore 153.0.7999
- License and third-party attributions: included by the upstream binary package
  and available from the projects above

MedNote does not modify PDFium or PDFiumCore. The original copyright notices,
license terms, and attribution files supplied by those packages remain
applicable.

## Win2D

- Project: https://github.com/microsoft/Win2D
- Package: Microsoft.Graphics.Win2D 1.4.0
- Binary package license: Microsoft Win2D EULA
  (https://www.microsoft.com/web/webpi/eula/eula_win2d_10012014.htm)
- Source repository license: MIT License

Win2D supplies the managed Direct2D interop and XAML image surface used by the
Reader's native page-display path.

## Lucide Reader icons

Version 0.468.0, matching the web Reader icon set. SVG outlines have been
converted to native XAML path geometry; no web renderer or runtime package is used.
Project: https://github.com/lucide-icons/lucide

ISC License

Copyright (c) for portions of Lucide are held by Cole Bemis 2013-2022 as part of Feather (MIT). All other copyright (c) for Lucide are held by Lucide Contributors 2022.

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
