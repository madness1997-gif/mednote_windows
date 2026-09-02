# PDF renderer fixtures

The local PowerShell generators create deterministic synthetic PDFs for edge cases that need exact geometry or page counts.

`pdfium-regression-corpus.json` adds a small set of upstream, non-generated PDFs from the Chromium PDFium test suite. Every URL is pinned to one commit and guarded by SHA-256. CI downloads these files into the runner cache; they are not committed to this repository or shipped in the application artifact.

The selected corpus covers embedded raster images, a large vector table, differing CropBox/MediaBox geometry, and AES password handling. The manifest records PDFium's pinned upstream license file, which contains its BSD 3-Clause terms and bundled Apache 2.0 notices.

`Get-PdfiumRegressionCorpus.ps1` is the only downloader. To refresh the corpus, update the source commit, URLs, checksums, and fixture expectations together, then run the Windows smoke workflow.
