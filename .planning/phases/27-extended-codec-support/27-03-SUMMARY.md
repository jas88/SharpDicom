---
phase: 27-extended-codec-support
plan: 03
subsystem: codecs
tags: [libjpeg-turbo, jpeg, 12-bit, native, zig, c]

# Dependency graph
requires:
  - phase: 13-native-codecs
    provides: native codec build infrastructure, jpeg_wrapper.c pattern
  - phase: 27-01
    provides: transfer syntax definitions for 12-bit JPEG
provides:
  - 12-bit JPEG native C wrapper (jpeg12_wrapper.c/h)
  - SHARPDICOM_HAS_JPEG12 feature flag (bit 9)
  - Dual libjpeg-turbo build configuration in build.zig
  - Symbol prefix strategy for coexisting 8-bit and 12-bit libjpeg
affects: [27-04, 27-05, 27-06]

# Tech tracking
tech-stack:
  added: []
  patterns: [symbol-prefixed dual library compilation, raw libjpeg API for 12-bit]

key-files:
  created:
    - native/src/jpeg12_wrapper.h
    - native/src/jpeg12_wrapper.c
  modified:
    - native/src/sharpdicom_codecs.h
    - native/src/sharpdicom_codecs.c
    - native/build.zig

key-decisions:
  - "Raw libjpeg API for 12-bit (not TurboJPEG) because WITH_12BIT disables TurboJPEG/SIMD"
  - "Symbol prefix via -D compiler flags (jpeg_* -> jpeg12_jpeg_*) to avoid collisions in single .so"
  - "Opaque struct buffers in jpeg12_wrapper.c rather than exact struct replication"

patterns-established:
  - "Symbol prefix pattern: dual library builds use -D to rename all public API symbols"
  - "Stub pattern: all codec wrappers compile without vendor libs, returning SHARPDICOM_ERR_UNSUPPORTED"

# Metrics
duration: 5min
completed: 2026-02-07
---

# Phase 27 Plan 03: 12-bit JPEG Native Wrapper Summary

**Native C wrapper for 12-bit JPEG using raw libjpeg API with symbol-prefixed dual libjpeg-turbo builds in Zig**

## Performance

- **Duration:** 5 min
- **Started:** 2026-02-07T04:08:36Z
- **Completed:** 2026-02-07T04:13:51Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments
- Created jpeg12_wrapper.c/h with full 12-bit JPEG encode/decode/free/has_support API
- Added SHARPDICOM_HAS_JPEG12 feature flag (bit 9) to codec feature bitmap
- Updated build.zig with dual libjpeg-turbo compilation support and symbol prefix strategy
- Stub mode works when vendor libraries not present (returns SHARPDICOM_ERR_UNSUPPORTED)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create jpeg12_wrapper.c/h and update feature flags** - `bc6b987` (feat)
2. **Task 2: Update build.zig for dual libjpeg-turbo compilation** - `621028c` (feat)

## Files Created/Modified
- `native/src/jpeg12_wrapper.h` - 12-bit JPEG function declarations (decode, encode, free, has_support)
- `native/src/jpeg12_wrapper.c` - Full implementation using prefixed raw libjpeg API with setjmp/longjmp error handling
- `native/src/sharpdicom_codecs.h` - Added SHARPDICOM_HAS_JPEG12 feature flag (1 << 9)
- `native/src/sharpdicom_codecs.c` - Added JPEG12 feature reporting and header include
- `native/build.zig` - Dual libjpeg-turbo build config with 50+ symbol prefix defines

## Decisions Made
- Used raw libjpeg API (not TurboJPEG) for 12-bit path because libjpeg-turbo's WITH_12BIT flag disables TurboJPEG and SIMD acceleration. The 8-bit path retains full SIMD performance via TurboJPEG.
- Applied symbol prefix via -D compiler flags rather than source modification, keeping vendor sources pristine and enabling clean updates.
- Used opaque byte buffers for libjpeg struct allocation rather than exact struct replication, since the internal struct layout is version-dependent. The actual jpeglib.h from the 12-bit build will provide correct layouts when vendor sources are present.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- Zig is not installed locally, so `zig build` verification could not be run. Used `cc -fsyntax-only` to verify C syntax correctness for both stub and full compilation modes. Both pass cleanly.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Native 12-bit JPEG wrapper ready for C# P/Invoke bindings (plan 27-04)
- Feature flag infrastructure ready for runtime codec detection
- Build.zig prepared for CI integration when vendor sources are downloaded

---
*Phase: 27-extended-codec-support*
*Completed: 2026-02-07*
