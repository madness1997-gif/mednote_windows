# M3 annotation acceptance

## Automated gates

- all web v6 annotation kind tokens round-trip through the native codec;
- unknown annotation kinds and fields survive add/delete/undo/redo operations;
- history is capped at 60 document snapshots and a new edit clears redo;
- annotation coordinates round-trip at Reader rotations 0/90/180/270;
- crop output is a structurally valid RGBA PNG;
- normal core tests, Windows build, publish and packaged render smoke remain
  required by CI.

## Manual Reader pass

1. Open a text PDF and add highlight, underline, strikeout and squiggly markup.
2. Add an area highlight over an image or table.
3. Draw a pen stroke, then erase both a stroke and a markup annotation.
4. Draw rectangle, ellipse and arrow objects.
5. Change zoom, fit mode, view mode and Reader rotation; verify every overlay
   stays anchored to the same PDF content.
6. Undo and redo at least one operation with toolbar buttons and Ctrl+Z/Ctrl+Y.
7. Crop a region and verify the status reports a Note-ready result.
8. Export the annotated PDF, reopen it outside MedNote, and verify annotations
   are visible while original text remains selectable.
9. Reopen MedNote and verify annotations persist in the Reader state.
