# Manual real-world PDF corpus

This directory is a **local-only** staging area for real documents used during
Reader stabilization. Do not commit copyrighted or patient-containing PDFs.
The repository ignores `*.pdf` here.

Keep a small representative corpus covering at least:

1. a 1,000–3,000 page textbook with mixed vector text and images;
2. journal PDFs with dense tables, multi-column text and embedded fonts;
3. scan-heavy PDFs;
4. mixed portrait/landscape documents;
5. files with existing annotations/forms;
6. unusually large images or page boxes;
7. password-protected PDFs that can be shared safely for testing.

Run the normal CI fixtures on every release candidate. Run this corpus and the
manual soak harness only when touching PDFium, Direct2D, virtualization,
selection/search, or before a milestone release; it is intentionally not part
of every push.

Example:

```powershell
./scripts/Invoke-ReaderManualSoak.ps1 `
  -AppPath ./artifacts/MedNote-Reader-Windows/MedNote.Reader.exe `
  -CorpusPath ./tests/manual-pdf-corpus `
  -MinutesPerFile 30
```

During each file, continuously scroll, jump to distant pages, zoom, search,
select/copy text, rotate, and switch Single/Continuous. Inspect the resulting
working-set/private-memory CSV for sustained upward drift rather than transient
render-cache peaks.
