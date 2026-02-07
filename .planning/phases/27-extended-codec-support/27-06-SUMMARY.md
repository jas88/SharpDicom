---
phase: 27-extended-codec-support
plan: 06
subsystem: native-codecs
tags: [ffmpeg, video-encoding, mpeg2, h264, hevc, stb_image, gpu-accel, c-api]

# Dependency graph
requires:
  - phase: 13-native-codecs-package
    provides: Native codec build infrastructure (build.zig, sharpdicom_codecs.h, video_wrapper.h)
  - phase: 27-01
    provides: Transfer syntax and UID definitions for video formats
provides:
  - Video encoder C API (MPEG2/H.264/HEVC) with quality presets and GPU fallback
  - stb_image wrapper for memory-based image loading (PNG/BMP/JPEG/TGA)
  - Build system integration for video_encoder.c and stb_image_wrapper.c
affects:
  - 27-07 (managed VideoEncoder P/Invoke bindings)
  - 27-08 (video encoding integration tests)

# Tech tracking
tech-stack:
  added: [stb_image v2.30]
  patterns: [handle-based encoder API matching decoder pattern, in-memory muxing via avio, GPU encoder cascade with CPU fallback]

key-files:
  created:
    - native/src/video_encoder.h
    - native/src/video_encoder.c
    - native/src/stb_image_wrapper.h
    - native/src/stb_image_wrapper.c
    - native/vendor/stb/stb_image.h
  modified:
    - native/build.zig

key-decisions:
  - "Used separate SHARPDICOM_WITH_FFMPEG_ENC flag for encoding (not shared with decode flag)"
  - "GPU encoder cascade: VideoToolbox > NVENC > VAAPI for platform portability"
  - "In-memory muxing via avio_write_buffer callback for zero-temp-file encoding"
  - "Annex-B raw bitstream for H.264/HEVC, MPEG-TS container for MPEG-2 or audio-muxed streams"
  - "stb_image configured with STBI_NO_STDIO and STBI_NO_HDR for minimal footprint"

patterns-established:
  - "Video encoder mirror pattern: encoder API mirrors decoder handle lifecycle"
  - "Quality preset system: Diagnostic/Review/Archive with per-codec CRF/bitrate defaults"
  - "Vendor single-header library integration: header in vendor/stb/, implementation in wrapper .c"

# Metrics
duration: 8min
completed: 2026-02-07
---

# Phase 27 Plan 06: Native Video Encoder and stb_image Wrapper Summary

**Video encoder C API with MPEG2/H.264/HEVC encoding, GPU-accelerated encoder detection (NVENC/VideoToolbox/VAAPI), quality presets, audio interleaving, and stb_image v2.30 integration for image loading**

## Performance

- **Duration:** 8 min
- **Started:** 2026-02-07T04:09:55Z
- **Completed:** 2026-02-07T04:17:46Z
- **Tasks:** 2
- **Files modified:** 6

## Accomplishments
- Full video encoder C API implemented: create/encode_frame/encode_audio/flush/get_output/destroy lifecycle
- GPU-accelerated encoder detection cascade: tries VideoToolbox, NVENC, VAAPI before falling back to libx264/libx265 CPU encoders
- Quality presets (Diagnostic CRF17/Review CRF23/Archive CRF28 for H.264) with CRF and bitrate mode overrides
- Audio encoding support (AAC/PCM) with SwrContext-based format conversion and interleaved muxing
- stb_image v2.30 vendored and wrapped with memory-only API for PNG/BMP/JPEG/TGA loading
- All code compiles cleanly in both stub mode and enabled mode with zero warnings

## Task Commits

Each task was committed atomically:

1. **Task 1: Create video_encoder.h/c** - `cd84e20` (feat)
2. **Task 2: Vendor stb_image and create wrapper** - `1c44606` (feat)

## Files Created/Modified
- `native/src/video_encoder.h` - Handle-based video encoder API declarations with quality/audio/hwaccel constants
- `native/src/video_encoder.c` - Full FFmpeg-based encoder implementation with GPU fallback and stub mode
- `native/src/stb_image_wrapper.h` - Memory-based image loading API declarations
- `native/src/stb_image_wrapper.c` - stb_image wrapper implementation with stub fallback
- `native/vendor/stb/stb_image.h` - Vendored stb_image v2.30 (public domain, 7988 lines)
- `native/build.zig` - Added video_encoder.c and stb_image_wrapper.c to all build targets

## Decisions Made
- **Separate encoding flag:** Used `SHARPDICOM_WITH_FFMPEG_ENC` rather than overloading `SHARPDICOM_HAS_FFMPEG` to allow decode-only builds without encoding dependencies (libavformat, libswresample, x264/x265).
- **GPU encoder ordering:** VideoToolbox listed first in the cascade (most common on macOS developer machines), followed by NVENC and VAAPI. This mirrors the platform-specific hardware availability.
- **In-memory output:** Used `avio_alloc_context` with a write callback and dynamic buffer rather than temp files. This is critical for DICOM embedding where the encoded bitstream goes directly into pixel data fragments.
- **Container format selection:** Raw Annex-B bitstream for H.264/HEVC when no audio is present (minimal overhead), MPEG-TS container when audio is interleaved or for MPEG-2 (requires container format).
- **stb_image vendor approach:** Checked in the full header file since it is a single public-domain header (unlike large source trees that are downloaded in CI). This ensures the build works without network access.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Video encoder C API is ready for managed P/Invoke bindings (plan 27-07)
- stb_image wrapper is ready for image sequence loading integration
- Build system properly configured for all 6 platform targets plus test executable
- Stub mode ensures clean builds even without FFmpeg or stb_image vendor libraries

---
*Phase: 27-extended-codec-support*
*Completed: 2026-02-07*
