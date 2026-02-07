---
phase: 27-extended-codec-support
plan: 12
status: complete
subsystem: native-build
tags: [documentation, native, libjpeg-turbo, 12-bit-jpeg, build-system]
dependency-graph:
  requires: [27-03, 27-04]
  provides: [native-build-documentation, 12-bit-jpeg-build-instructions]
  affects: []
tech-stack:
  added: []
  patterns: [optional-vendor-stub-fallback]
key-files:
  created:
    - native/BUILD-REQUIREMENTS.md
  modified: []
decisions:
  - id: doc-scope
    description: "Documented all vendor libraries (not just 12-bit JPEG) since they share the same pattern"
    rationale: "Single reference document for the entire native build system"
metrics:
  duration: ~3min
  completed: 2026-02-07
---

# Plan 27-12: Document Native Build Requirements

One-liner: BUILD-REQUIREMENTS.md documenting all optional vendor library flags, 12-bit JPEG symbol prefix approach, and stub fallback behavior.

## Changes Made

- Created `native/BUILD-REQUIREMENTS.md` covering:
  - Overview of the optional vendor library build model
  - Required tools (Zig 0.13.0+)
  - Table of all 8 optional vendor libraries with build flags, vendor paths, and source URLs
  - Step-by-step instructions for enabling native 12-bit JPEG (clone source, set `have_libjpeg12 = true`, build)
  - Explanation of the symbol prefix approach (`jpeg_*` renamed to `jpeg12_jpeg_*`) for 8-bit/12-bit coexistence
  - 12-bit build limitations (no TurboJPEG, no SIMD)
  - Fallback behavior: stub returns error codes, `NativeCodecs.HasFeature(NativeCodecFeature.Jpeg12Bit)` returns false, managed `JpegExtendedCodec` handles 12-bit JPEG
  - CI notes for adding a native 12-bit JPEG test variant

## Verification

- `native/BUILD-REQUIREMENTS.md` exists
- Document references `have_libjpeg12`, `vendor/libjpeg-turbo/src`, and symbol prefix approach
- `dotnet build` passes: 0 warnings, 0 errors (documentation-only change)

## Deviations from Plan

None -- plan executed exactly as written.

## Gaps Closed

- **Gap 3 from 27-VERIFICATION.md**: "Native 12-bit JPEG build requirements not documented" -- CLOSED. Developers can now follow the documented steps to enable native 12-bit JPEG support, and the fallback behavior (managed `JpegExtendedCodec`) is clearly explained.

## Commits

| Hash | Message |
|------|---------|
| b1ae119 | docs(27-12): document native build requirements for optional vendor libraries |
