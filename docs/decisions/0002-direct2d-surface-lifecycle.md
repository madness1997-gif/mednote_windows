# ADR 0002 — Direct2D surface lifecycle

## Status

Accepted for the P1 Reader hardening baseline.

## Context

PDFium returns a managed BGRA page buffer. P0 removed the PNG encode/decode
round trip and uploads that buffer through Win2D to a Direct2D-backed
`CanvasImageSource`. GPU driver resets, adapter changes, and XAML composition
surface loss can invalidate that image source while the PDF page and its
managed buffer remain valid.

## Decision

- Keep one observed Win2D shared `CanvasDevice` in the surface factory.
- Report Win2D exceptions recognized by `CanvasDevice.IsDeviceLost` through
  `RaiseDeviceLost` and subscribe to `CanvasDevice.DeviceLost`.
- Subscribe realized page presenters to both device invalidation and
  `CompositionTarget.SurfaceContentsLost`.
- Coalesce recreation requests on each presenter's UI dispatcher.
- Recreate only the GPU/XAML surface from the existing budgeted BGRA bytes;
  do not ask PDFium to rasterize the page again.
- Keep device and surface objects outside persisted Reader state.

## Verification

The packaged-app smoke test forces a surface invalidation after the first
presentation and requires the target page to be presented a second time. The
same workflow validates the Win2D runtime in the published artifact and runs
the adversarial PDF corpus.

## Consequences

Recovery cost is one BGRA upload for each realized page, bounded by
virtualization and the existing 192 MiB CPU/GPU surface budget. A true hardware
adapter reset still depends on Win2D returning a replacement shared device, but
the application no longer retains the invalid device or crashes on the first
recognized device-loss exception.
