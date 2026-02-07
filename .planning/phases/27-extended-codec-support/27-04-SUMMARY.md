---
phase: 27-extended-codec-support
plan: 04
subsystem: codecs
tags: [jpeg, 12-bit, native, libjpeg-turbo, pinvoke, interop]

# Dependency graph
requires:
  - phase: 27-01
    provides: Native codec infrastructure (NativeMethods, NativeCodecs, CodecRegistry)
  - phase: 27-03
    provides: Managed JpegExtendedCodec for JPEG Extended transfer syntax
provides:
  - NativeJpeg8Codec explicit 8-bit native JPEG codec
  - NativeJpeg12Codec native 12-bit JPEG codec via libjpeg-turbo
  - jpeg12_* P/Invoke declarations (decode, encode, free, has_support)
  - Jpeg12Bit feature detection in NativeCodecFeature enum
  - NativeJpeg12Codec registered at PriorityNative for JPEGExtended
affects: [27-05, 27-07, 27-08]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "12-bit JPEG uses uint16_t output (2 bytes per sample) even though only 12 bits used"
    - "Explicit 8-bit codec (NativeJpeg8Codec) separate from general NativeJpegCodec"

key-files:
  created:
    - src/SharpDicom.Codecs/Codecs/NativeJpeg8Codec.cs
    - src/SharpDicom.Codecs/Codecs/NativeJpeg12Codec.cs
  modified:
    - src/SharpDicom.Codecs/Interop/NativeMethods.cs
    - src/SharpDicom.Codecs/NativeCodecs.cs

key-decisions:
  - "NativeJpeg12Codec registered only when Jpeg12Bit feature detected, preserving managed fallback"
  - "12-bit decode bytesWritten calculated as width*height*components*2 for uint16_t output"
  - "NativeJpeg8Codec not separately registered to avoid conflict with existing NativeJpegCodec"

patterns-established:
  - "12-bit P/Invoke pattern: separate jpeg12_* entry points without colorspace parameter"
  - "Feature-gated codec registration with EnableJpeg12Bit toggle"

# Metrics
duration: 5min
completed: 2026-02-07
---

# Phase 27 Plan 04: Native 12-bit JPEG Codec Summary

**NativeJpeg12Codec wrapping libjpeg-turbo 12-bit via jpeg12_decode/encode P/Invoke, plus explicit NativeJpeg8Codec and Jpeg12Bit feature detection**

## Performance

- **Duration:** 5 min
- **Started:** 2026-02-07T04:22:07Z
- **Completed:** 2026-02-07T04:27:47Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- Added P/Invoke declarations for jpeg12_decode, jpeg12_encode, jpeg12_free, jpeg12_has_support in both LibraryImport and DllImport forms
- Created NativeJpeg12Codec implementing IPixelDataCodec for TransferSyntax.JPEGExtended with 8/12-bit support
- Created NativeJpeg8Codec as explicit 8-bit native JPEG codec
- Added Jpeg12Bit feature flag to NativeFeatures (1 << 9) and NativeCodecFeature enum
- Registered NativeJpeg12Codec at PriorityNative to override managed JpegExtendedCodec when native library available
- All 2263 tests pass with zero regressions

## Task Commits

Each task was committed atomically:

1. **Task 1: Add jpeg12_* P/Invoke declarations and Jpeg12Bit feature** - `e7a4506` (feat)
2. **Task 2: Create NativeJpeg8Codec and NativeJpeg12Codec** - `fcdf6ad` (feat)

## Files Created/Modified
- `src/SharpDicom.Codecs/Codecs/NativeJpeg8Codec.cs` - Explicit 8-bit native JPEG codec delegating to jpeg_decode/jpeg_encode
- `src/SharpDicom.Codecs/Codecs/NativeJpeg12Codec.cs` - Native 12-bit JPEG codec delegating to jpeg12_decode/jpeg12_encode
- `src/SharpDicom.Codecs/Interop/NativeMethods.cs` - Added jpeg12_* P/Invoke declarations and Jpeg12Bit NativeFeatures flag
- `src/SharpDicom.Codecs/NativeCodecs.cs` - Added Jpeg12Bit feature detection, EnableJpeg12Bit property, codec registration

## Decisions Made
- Kept existing NativeJpegCodec registration unchanged to maintain backward compatibility; NativeJpeg8Codec exists as an explicit named variant but is not registered separately
- NativeJpeg12Codec uses `jpeg12_decode` without a colorspace parameter (unlike 8-bit `jpeg_decode`), matching the native 12-bit API which handles colorspace internally
- 12-bit encode raw size guard uses `* 2` multiplier for uint16_t sample size

## Deviations from Plan
None - plan executed exactly as written.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Native 12-bit JPEG pipeline complete from C# through P/Invoke
- Ready for integration testing with actual 12-bit DICOM files (future plan)
- NativeJpeg8Codec available if needed for explicit 8-bit codec selection
- Feature detection via HasFeature(NativeCodecFeature.Jpeg12Bit) enables graceful fallback

---
*Phase: 27-extended-codec-support*
*Completed: 2026-02-07*
