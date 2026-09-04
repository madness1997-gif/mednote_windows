# Web compatibility contract

The native Reader must continue to recognize records created by `madness1997-gif/mednote-reader` schema v6.

## Stable document identity

The web app uses:

```text
doc-${FNV1a36("<name>:<size>:<lastModifiedMs>")}
```

`DocumentIdentity.Create` is an exact UTF-16/FNV-1a port. The regression tests include Vietnamese text to prevent an accidental UTF-8 implementation from changing IDs.

## Reader payload

The following JSON fields retain their web names and values:

| Field | Native handling |
|---|---|
| `page` | read/write |
| `zoom` | read/write, clamped to 0.55–2.5 |
| `fitMode` | `page` or `width` |
| `rotation` | preserved and normalized |
| `viewMode` | `single` or `continuous` |
| `bookmarks` | read/write |
| `annotations` | typed native editing with exact web v6 kind/field names; unknown records and fields remain opaque and preserved |

The native-only `position` extension stores an anchor page, a normalized offset within that page, and horizontal offset. It does not alter the existing v6 Reader payload.

## Note-library import boundary

The native app shares hierarchy, document graph and Reader contracts with web
v6, but it does not use the web editor's SheetContent JSON at runtime. Windows
owns one RTF document per Sheet.

One-way web import validates all of these records before native cutover:

- Workspace → Notebook → Section → Page → Sheet metadata;
- one web JSON `SheetContent` record per Sheet;
- Document, Context, Group, Link, and LinkRelation records;
- v2 Drive manifest hashes;
- unknown annotation fields;
- successful conversion of every web Sheet to native RTF.

The exact web DTO and backup hash codec are isolated under
`Compatibility.WebV6`. A failed conversion cannot replace the live native
manifest. M4 does not promise reverse RTF→web conversion or direct editing of a
web SheetContent record.
