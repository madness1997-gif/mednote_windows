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

## Full v6 library migration

Milestone 1 intentionally keeps a small Reader library rather than writing the entire note hierarchy. Full import/export must later stage and validate all of these records before cutover:

- Workspace → Notebook → Section → Page → Sheet metadata;
- one `SheetContent` record per Sheet;
- Document, Context, Group, Link, and LinkRelation records;
- v2 Drive manifest hashes;
- unknown annotation fields.

Until that validator is implemented, the native app must never rewrite a complete web v6 backup.
