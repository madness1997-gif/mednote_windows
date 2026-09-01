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
