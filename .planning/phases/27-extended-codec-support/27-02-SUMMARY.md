---
phase: 27-extended-codec-support
plan: 02
subsystem: codecs
tags: [jpeg, jpeg-extended, sof1, 12-bit, dct, lossy, dicom-transfer-syntax]

# Dependency graph
requires:
  - phase: 27-01
    provides: TransferSyntax.JPEGExtended definition, CompressionType.JPEGExtended enum
provides:
  - JpegExtendedCodec (IPixelDataCodec for DICOM TS 1.2.840.10008.1.2.4.51)
  - JpegExtendedDecoder (pure C# 8/12-bit JPEG decoder with SOF1)
  - JpegExtendedEncoder (pure C# 8/12-bit JPEG encoder with SOF1)
  - Codec registration in CodecInitializer
affects: [27-03, 27-04, 27-05, 27-06]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Extended JPEG codec pattern: parameterized precision (8/12-bit) with int[] component buffers"
    - "12-bit output uses ushort little-endian in DICOM destination buffers"

key-files:
  created:
    - src/SharpDicom/Codecs/Jpeg/JpegExtendedDecoder.cs
    - src/SharpDicom/Codecs/Jpeg/JpegExtendedEncoder.cs
    - src/SharpDicom/Codecs/Jpeg/JpegExtendedCodec.cs
  modified:
    - src/SharpDicom/Codecs/CodecInitializer.cs
    - tests/SharpDicom.Tests/Codecs/CodecRegistryIntegrationTests.cs

key-decisions:
  - "Used int[] component buffers instead of byte[] to handle 12-bit precision without overflow"
  - "JpegLosslessCodec already supports 16-bit precision -- no changes needed"
  - "Decoder accepts both SOF0 and SOF1 for maximum compatibility"

patterns-established:
  - "Extended precision codec: use int[] for intermediate component storage, write ushort LE for 12-bit output"
  - "12-bit level shift is 2048 (2^11), 12-bit max sample is 4095 (2^12-1)"

# Metrics
duration: 9min
completed: 2026-02-07
---

# Phase 27 Plan 02: JPEG Extended Codec Summary

**Pure C# managed 8/12-bit JPEG Extended codec (SOF1, Process 2,4) with full IPixelDataCodec implementation and CodecInitializer registration**

## Performance

- **Duration:** 9 min
- **Started:** 2026-02-07T04:07:52Z
- **Completed:** 2026-02-07T04:17:00Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments
- JpegExtendedDecoder handles SOF1 marker with 8-bit and 12-bit precision, including grayscale and RGB color images
- JpegExtendedEncoder produces SOF1 JPEG bitstream with configurable precision, 16-bit DQT entries for 12-bit mode
- JpegExtendedCodec implements full IPixelDataCodec contract with encode, decode, validate, and async variants
- Registered in CodecInitializer, bringing total managed codecs to 11
- JpegLosslessCodec verified to already handle up to 16-bit precision (no changes needed)
- Zero build warnings across all target frameworks, zero test regressions

## Task Commits

Each task was committed atomically:

1. **Task 1: Create JpegExtendedDecoder and JpegExtendedEncoder** - `871b3bc` (feat)
2. **Task 2: Create JpegExtendedCodec, extend JpegLosslessCodec, register** - `883e635` (feat)

## Files Created/Modified
- `src/SharpDicom/Codecs/Jpeg/JpegExtendedDecoder.cs` - Pure C# decoder for SOF1 (Extended Sequential DCT), handles 8-bit and 12-bit precision
- `src/SharpDicom/Codecs/Jpeg/JpegExtendedEncoder.cs` - Pure C# encoder producing SOF1 JPEG with configurable precision
- `src/SharpDicom/Codecs/Jpeg/JpegExtendedCodec.cs` - IPixelDataCodec implementation for TransferSyntax.JPEGExtended
- `src/SharpDicom/Codecs/CodecInitializer.cs` - Added JpegExtendedCodec registration
- `tests/SharpDicom.Tests/Codecs/CodecRegistryIntegrationTests.cs` - Updated codec count (10->11) and added JPEGExtended assertion

## Decisions Made
- Used `int[]` for intermediate component buffers in decoder/encoder to handle 12-bit precision without truncation (byte[] only holds 0-255)
- Decoder accepts both SOF0 and SOF1 markers for compatibility since some encoders may use SOF0 even in Extended transfer syntax
- JpegLosslessCodec was verified to already support 2-16 bit precision via existing LosslessHuffman categories 0-16; no modifications needed
- For 12-bit DQT entries, encoder uses 16-bit big-endian format (precision nibble = 1) per ITU-T T.81

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Updated codec registry count test**
- **Found during:** Task 2 (Codec registration)
- **Issue:** `GetRegisteredTransferSyntaxes_HasExpectedCount` test expected 10 codecs but we added 1 new
- **Fix:** Updated expected count from 10 to 11 and added JPEGExtended to inclusion test
- **Files modified:** tests/SharpDicom.Tests/Codecs/CodecRegistryIntegrationTests.cs
- **Verification:** All 2263 tests pass (2209 succeeded, 54 skipped, 0 failed)
- **Committed in:** 883e635 (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 bug)
**Impact on plan:** Test count update was necessary for correctness. No scope creep.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- JpegExtendedCodec is fully registered and functional for managed 8/12-bit JPEG compression
- Ready for subsequent plans to add more codec types (MPEG2, JPEG-XL, etc.)
- All 11 codecs registered and passing tests

---
*Phase: 27-extended-codec-support*
*Completed: 2026-02-07*
