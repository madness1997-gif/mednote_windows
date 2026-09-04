# ADR 0005: Native RTF owns Windows Note content

## Decision

Use the Windows RichEdit/RTF document as the sole editable Sheet-content model
in the native application. Continue sharing hierarchy, document-link and Reader
contracts where those concepts are platform-neutral. Keep the web v6
SheetContent JSON model behind a one-way import adapter.

## Rationale

Mirroring the web editor's block/DOM serialization would make the native app
carry two editing models or rebuild browser behavior in C#. That adds mapping
code to every edit, makes Windows features depend on web implementation detail,
and weakens the reason for a native rewrite.

RichEdit already owns selection, text runs, paragraphs, lists, tables, undo and
RTF serialization on Windows. Persisting its RTF output directly keeps one
source of truth and gives M4.2 a narrow editor/repository handoff.

## Consequences

- native Sheet blobs are raw UTF-8 RTF addressed by SHA-256;
- JSON remains the manifest format for hierarchy and document metadata;
- web v6 backup parsing and hash verification stay exact but isolated;
- importing web notes requires an explicit converter for every Sheet;
- conversion failure occurs before native atomic replacement;
- no automatic RTF→web round-trip is promised by M4;
- Reader annotation JSON compatibility is unchanged by this decision.
