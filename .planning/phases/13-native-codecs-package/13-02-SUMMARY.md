---
phase: 13-native-codecs-package
plan: 02
subsystem: native-codecs
tags: [libjpeg-turbo, turbojpeg, jpeg, dicom-codecs, c-api]

# Dependency graph
requires:
  - phase: 13-01
    provides: Native build infrastructure (Zig, CI workflow, C API header)
provides:
  - JPEG wrapper for libjpeg-turbo TurboJPEG API
  - 8-bit baseline JPEG encode/decode
  - 12-bit extended JPEG stub (requires special library build)
  - CI workflow downloads for libjpeg-turbo sources
affects: [13-04-PLAN (managed interop)]

# Tech tracking
tech-stack:
  added: [libjpeg-turbo 3.0.4, TurboJPEG API]
  patterns: [thread-local handles, lazy initialization, extern set_error]

key-files:
  created:
    - native/src/jpeg_wrapper.h
    - native/src/jpeg_wrapper.c
    - native/vendor/libjpeg-turbo/.gitkeep
  modified:
    - native/vendor/README.md
    - native/src/sharpdicom_codecs.c
    - native/build.zig
    - .github/workflows/native-build.yml

key-decisions:
  - "TurboJPEG API over raw libjpeg for simplified high-performance access"
  - "Thread-local handles for decompression/compression (lazy init)"
  - "TJFLAG_ACCURATEDCT for medical imaging quality"
  - "12-bit stub until special library build available"
  - "Extern set_error from sharpdicom_codecs.c for wrapper error reporting"

patterns-established:
  - "JpegColorspace enum: RGB=0, YBR=1, GRAY=2, CMYK=3, UNKNOWN=-1"
  - "JpegSubsampling enum: 444=0, 422=1, 420=2, GRAY=3, 440=4, 411=5"
  - "Error codes: JPEG_ERR_* in -100 range"
  - "Header-only decode via jpeg_decode_header()"

# Metrics
duration: 5min
completed: 2026-01-29
---

# Phase 13 Plan 02: libjpeg-turbo JPEG Wrapper Summary

**TurboJPEG wrapper for high-performance JPEG baseline and extended codec support**

## Performance

- **Duration:** 5 min
- **Started:** 2026-01-30T02:31:42Z
- **Completed:** 2026-01-30T02:36:42Z
- **Tasks:** 2
- **Files created:** 3
- **Files modified:** 4

## Accomplishments

- Created JPEG wrapper header with colorspace and subsampling enums
- Implemented jpeg_decode() using TurboJPEG API with header parsing
- Implemented jpeg_encode() with quality and subsampling parameters
- Added jpeg_decode_header() for dimension extraction without full decode
- Stubbed 12-bit JPEG functions (requires library built with -DWITH_12BIT)
- Set up vendor directory with libjpeg-turbo 3.0.4 documentation
- Updated CI workflow to download and cache libjpeg-turbo source
- Integrated JPEG wrapper into Zig build system with SHARPDICOM_WITH_JPEG flag

## Task Commits

Each task was committed atomically:

1. **Task 1: Create libjpeg-turbo vendoring setup** - `e925a94` (chore)
2. **Task 2: Implement JPEG wrapper** - `cf37b0a` (feat)

## Files Created/Modified

### Created
- `native/src/jpeg_wrapper.h` - JPEG wrapper API declarations (190 lines)
  - JpegColorspace enum (RGB, YBR, GRAY, CMYK, UNKNOWN)
  - JpegSubsampling enum (444, 422, 420, GRAY, 440, 411)
  - JPEG-specific error codes (-100 to -103)
  - 8-bit and 12-bit function declarations

- `native/src/jpeg_wrapper.c` - JPEG wrapper implementation (471 lines)
  - TurboJPEG API declarations (for header-only build)
  - Thread-local handle management (lazy init)
  - jpeg_decode() with header parsing and colorspace conversion
  - jpeg_encode() with quality and subsampling support
  - 12-bit stubs with runtime capability check

- `native/vendor/libjpeg-turbo/.gitkeep` - Placeholder for CI downloads

### Modified
- `native/vendor/README.md` - Added libjpeg-turbo documentation
- `native/src/sharpdicom_codecs.c` - Made set_error() extern, added JPEG feature flag
- `native/build.zig` - Added jpeg_wrapper.c and turbojpeg linkage
- `.github/workflows/native-build.yml` - Added libjpeg-turbo download for all platforms

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| TurboJPEG API | Simpler than raw libjpeg, SIMD-optimized by default |
| Thread-local handles | Avoid handle creation overhead per call |
| TJFLAG_ACCURATEDCT | Medical imaging requires highest quality DCT |
| 12-bit as stub | Most distributions don't build with -DWITH_12BIT |
| Extern set_error | Shared error mechanism across all wrappers |

## API Reference

### 8-bit Functions
```c
int jpeg_decode(const uint8_t* input, int inputLen,
                uint8_t* output, int outputLen,
                int* width, int* height, int* components,
                int colorspace);

int jpeg_decode_header(const uint8_t* input, int inputLen,
                       int* width, int* height, int* components,
                       int* subsampling);

int jpeg_encode(const uint8_t* input, int width, int height, int components,
                uint8_t** output, int* outputLen,
                int quality, int subsamp);

void jpeg_free(uint8_t* buffer);
```

### 12-bit Functions (Stub)
```c
int jpeg_decode_12bit(const uint8_t* input, int inputLen,
                      uint16_t* output, int outputLen,
                      int* width, int* height, int* components);

int jpeg_encode_12bit(const uint16_t* input, int width, int height, int components,
                      uint8_t** output, int* outputLen,
                      int quality);

int jpeg_has_12bit_support(void);
```

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None - plan executed smoothly.

## User Setup Required

For local development with JPEG support:
- macOS: `brew install jpeg-turbo`
- Ubuntu/Debian: `apt install libturbojpeg0-dev`
- Windows: Download from https://libjpeg-turbo.org/

## Next Phase Readiness

- JPEG wrapper ready for managed P/Invoke integration (13-04)
- CI will build with libjpeg-turbo linked
- 12-bit support available if library rebuilt with -DWITH_12BIT=1
- Error messages propagate via thread-local set_error()

---
*Phase: 13-native-codecs-package*
*Completed: 2026-01-29*
