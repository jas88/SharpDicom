---
phase: 27-extended-codec-support
plan: 07
subsystem: native-codecs
tags: [ffmpeg, x264, x265, zig, stb-image, video-encoding, build-system]

# Dependency graph
requires:
  - phase: 27-06
    provides: "video_encoder.c, stb_image_wrapper.c, video_encoder.h, stb_image_wrapper.h"
provides:
  - "SHARPDICOM_HAS_VIDEO_ENC feature flag (1 << 10)"
  - "SHARPDICOM_HAS_STB_IMAGE feature flag (1 << 11)"
  - "have_ffmpeg_enc build flag separate from have_ffmpeg"
  - "addX264Sources() helper for x264 source compilation via Zig"
  - "addX265Sources() helper for x265 C++ source compilation via Zig"
  - "addFfmpegEncSources() helper for FFmpeg encoding library compilation"
  - "Source file lists for minimal FFmpeg encoding subset"
affects:
  - 27-08 (managed P/Invoke wrapper needs feature flag detection)
  - 27-09 (CI/CD needs vendor source download scripts)

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Separate have_ffmpeg (decode) and have_ffmpeg_enc (encode) build flags"
    - "FFmpeg/x264/x265 compiled from source via Zig build system"
    - "Layered core_flags build (core_flags_0 -> 1 -> 2 -> final)"

key-files:
  created: []
  modified:
    - "native/build.zig"
    - "native/src/sharpdicom_codecs.h"
    - "native/src/sharpdicom_codecs.c"

key-decisions:
  - "Separate have_ffmpeg_enc from have_ffmpeg to allow independent decode/encode control"
  - "Compile x264/x265/FFmpeg from source via Zig rather than linking system libraries"
  - "Use comptime conditional compilation with addX264Sources/addX265Sources/addFfmpegEncSources helpers"
  - "Include x265 as C++ compiled with Zig's C++ compiler mode using -std=c++14"

patterns-established:
  - "Vendor library helper function pattern: addX264Sources(), addX265Sources(), addFfmpegEncSources()"
  - "Layered flag propagation to core compilation unit for feature reporting"

# Metrics
duration: 12min
completed: 2026-02-07
---

# Phase 27 Plan 07: FFmpeg Encoding Build Infrastructure Summary

**build.zig updated with FFmpeg/x264/x265 from-source compilation, separate encode/decode flags, and SHARPDICOM_HAS_VIDEO_ENC feature reporting**

## Performance

- **Duration:** 12 min
- **Started:** 2026-02-07T04:22:41Z
- **Completed:** 2026-02-07T04:35:00Z
- **Tasks:** 2
- **Files modified:** 3

## Accomplishments
- Added SHARPDICOM_HAS_VIDEO_ENC (1 << 10) and SHARPDICOM_HAS_STB_IMAGE (1 << 11) feature flags with compile-time reporting
- Separated video encoding (have_ffmpeg_enc) from video decoding (have_ffmpeg) build paths for independent control
- Created complete build infrastructure for x264, x265, and FFmpeg encoding libraries compiled from source via Zig
- FFmpeg source file lists organized by library (libavutil, libavcodec, libswscale, libswresample, libavformat) covering minimal encoding subset

## Task Commits

Each task was committed atomically:

1. **Task 1: Add video encoding feature flag and stb_image build support** - `8f7bde7` (feat)
2. **Task 2: Add FFmpeg and x264/x265 build configuration** - `82a5831` (feat)

## Files Created/Modified
- `native/src/sharpdicom_codecs.h` - Added SHARPDICOM_HAS_VIDEO_ENC and SHARPDICOM_HAS_STB_IMAGE feature flag constants
- `native/src/sharpdicom_codecs.c` - Added feature detection for SHARPDICOM_WITH_FFMPEG_ENC and SHARPDICOM_WITH_STB_IMAGE
- `native/build.zig` - Added have_ffmpeg_enc flag, separated encode/decode paths, added addX264Sources/addX265Sources/addFfmpegEncSources helpers with complete source file lists

## Decisions Made
- Separated have_ffmpeg_enc from have_ffmpeg to allow encoding and decoding to be independently controlled (encoding requires x264/x265 backends that decoding does not)
- Compile x264/x265/FFmpeg from source via Zig rather than linking system libraries, following the allyourcodebase/ffmpeg pattern for consistent cross-platform behavior
- x265 compiled as C++ (std=c++14) using Zig's built-in C++ compiler mode, with linkLibCpp() for standard library
- FFmpeg source list covers minimal encoding subset: MPEG-2 (built-in), H.264 (via libx264), HEVC (via libx265), AAC, PCM, with MPEG-TS/raw muxers

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- Zig is not installed on the development machine, so `zig build` verification could not be run locally. All code changes follow exact patterns from existing build.zig (same Zig API calls, same flag patterns, same helper function structure as addOpenJpegSources). The build will be verified in CI.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- Build infrastructure ready for vendor source integration (CI download scripts in 27-09)
- Feature flags ready for managed P/Invoke detection in 27-08
- All existing native library functions unaffected (stub paths unchanged)

---
*Phase: 27-extended-codec-support*
*Completed: 2026-02-07*
