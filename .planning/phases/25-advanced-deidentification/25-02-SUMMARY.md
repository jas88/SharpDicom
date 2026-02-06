---
phase: 25-advanced-deidentification
plan: 02
subsystem: native-codecs
tags: [tesseract, ocr, pinvoke, native, zig]
dependency-graph:
  requires: [13]
  provides: [tesseract-native-wrapper, tesseract-pinvoke, tesseract-safehandle]
  affects: [25-03, 25-04]
tech-stack:
  added: []
  patterns: [stub-mode-compilation, dual-pinvoke, safehandle-lifecycle]
key-files:
  created:
    - native/src/tesseract_wrapper.h
    - native/src/tesseract_wrapper.c
    - src/SharpDicom.Codecs/Interop/TesseractNativeMethods.cs
    - src/SharpDicom.Codecs/Interop/TesseractHandle.cs
  modified:
    - native/build.zig
    - native/src/sharpdicom_codecs.c
    - native/src/sharpdicom_codecs.h
    - src/SharpDicom.Codecs/Interop/NativeMethods.cs
decisions:
  - id: tesseract-feature-bit
    choice: "SHARPDICOM_HAS_TESSERACT = 1 << 8 in native header, Tesseract = 1 << 5 in managed NativeFeatures enum"
    reason: "Native header bits 0-7 already allocated; managed enum uses its own numbering"
metrics:
  duration: 6m
  completed: 2026-02-06
---

# Phase 25 Plan 02: Tesseract Native Wrapper and P/Invoke Layer Summary

Native Tesseract C wrapper with full/stub modes, Zig build integration, managed P/Invoke declarations (LibraryImport + DllImport), and SafeHandle lifecycle management for TessBaseAPI.

## What Was Done

### Task 1: Native Tesseract C Wrapper

Created `tesseract_wrapper.h` and `tesseract_wrapper.c` implementing a thin C wrapper around the Tesseract 5.x C API:

- **TessDetectionResult struct**: Shared between native and managed code, carrying bounding box (left, top, right, bottom), confidence score, and text pointer
- **10 wrapper functions**: tess_create, tess_delete, tess_init, tess_set_image, tess_set_page_seg_mode, tess_recognize, tess_get_detections, tess_free_text, tess_clear, tess_available
- **Stub mode**: When `SHARPDICOM_WITH_TESSERACT` is not defined, all functions compile as stubs that report errors via thread-local `set_error()`
- **Full mode**: Wraps TessBaseAPI lifecycle, word-level result iteration with bounding boxes and confidence
- Follows the exact pattern established by jpeg_wrapper.c, jls_wrapper.c, etc.

### Task 2: Build Integration, Wiring, and Managed P/Invoke

**Zig build (build.zig):**
- Added `have_tesseract = false` alongside existing vendor flags
- Added tesseract_wrapper.c to all three build targets: cross-compile loop, test executable, native single-platform
- When `have_tesseract` is true: compiles with `-DSHARPDICOM_WITH_TESSERACT`, links Tesseract and Leptonica
- When false: compiles as stub (no external dependencies)

**Native wiring (sharpdicom_codecs.c/h):**
- Added `SHARPDICOM_HAS_TESSERACT (1 << 8)` to feature bitmap constants in header
- Added `#include "tesseract_wrapper.h"` to sharpdicom_codecs.c
- Added Tesseract feature flag to `sharpdicom_features()` function (conditional on `SHARPDICOM_WITH_TESSERACT`)

**Managed P/Invoke (TesseractNativeMethods.cs):**
- 10 P/Invoke declarations using `LibraryImport` on NET7+ and `DllImport` on netstandard2.0
- UTF-8 string marshalling for `tess_init` (datapath and language parameters)
- `TessDetection` struct with `StructLayout(LayoutKind.Sequential)` matching native layout
- All functions target the same `sharpdicom_codecs` native library

**SafeHandle (TesseractHandle.cs):**
- `TesseractHandle` extends `SafeHandle` with `ReleaseHandle` calling `tess_delete`
- Factory method `Create()` wraps `tess_create()` call
- Thread-safety note: TessBaseAPI handles are per-thread

**NativeFeatures enum:**
- Added `Tesseract = 1 << 5` to the managed feature flags enum

## Decisions Made

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Feature bit allocation | Native: `1 << 8`, Managed: `1 << 5` | Native bits 0-7 already assigned to existing features; managed enum uses its own numbering scheme |
| Constructor visibility | `internal` on TesseractHandle | CA1419 requires parameterless constructor to be as visible as containing type |
| String marshalling | `BestFitMapping=false, ThrowOnUnmappableChar=true` for DllImport | CA2101 compliance for netstandard2.0 target |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed CA1419 SafeHandle constructor visibility**
- Found during: Task 2 build verification
- Issue: CA1419 requires parameterless constructor to be as visible as containing type; plan specified `private`
- Fix: Changed to `internal` visibility
- Files modified: TesseractHandle.cs
- Commit: a68c524

**2. [Rule 1 - Bug] Fixed CA2101 string marshalling warning**
- Found during: Task 2 build verification
- Issue: DllImport for tess_init lacked proper marshalling attributes for CA2101 compliance
- Fix: Added `BestFitMapping = false, ThrowOnUnmappableChar = true` to DllImport
- Files modified: TesseractNativeMethods.cs
- Commit: a68c524

## Verification Results

| Check | Result |
|-------|--------|
| `dotnet build` (full solution) | 0 warnings, 0 errors |
| `dotnet test` (full suite) | 2159 passed, 54 skipped, 0 failed |
| tesseract_wrapper.c contains tess_create | Yes (2 occurrences) |
| tesseract_wrapper.h contains tess_create | Yes (9 occurrences - declarations + docs) |
| sharpdicom_codecs.c includes tesseract_wrapper.h | Yes |
| sharpdicom_codecs.c checks SHARPDICOM_WITH_TESSERACT | Yes |
| P/Invoke uses LibraryImport on NET7+ | Yes (10 declarations) |
| P/Invoke uses DllImport on netstandard2.0 | Yes (10 declarations) |
| TesseractHandle calls tess_delete | Yes |
| NativeFeatures.Tesseract flag exists | Yes |

## Commits

| Hash | Description |
|------|-------------|
| 88825e9 | feat(25-02): native Tesseract C wrapper with stub support |
| a68c524 | feat(25-02): Zig build integration, sharpdicom_codecs wiring, and managed P/Invoke layer |

## Next Phase Readiness

Plan 25-03 (OcrTextScanner) can now use TesseractHandle and TesseractNativeMethods to build the managed OCR scanning layer. The SafeHandle pattern ensures proper cleanup. The stub mode allows the scanner to gracefully report Tesseract unavailability without crashing.
