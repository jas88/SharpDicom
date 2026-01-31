---
phase: 13-native-codecs-package
plan: 04
subsystem: native-codecs
tags: [charls, jpeg-ls, ffmpeg, video, mpeg, hevc, h264, native, c]
dependency-graph:
  requires: [13-01]
  provides:
    - jls_wrapper.h
    - jls_wrapper.c
    - video_wrapper.h
    - video_wrapper.c
  affects: [13-05]
tech-stack:
  added: [CharLS, FFmpeg, libavcodec, libswscale]
  patterns:
    - Handle-based API for video decoder (stateful multi-frame)
    - Parameter struct pattern for encode/decode options
    - Stub implementation pattern for optional dependencies
key-files:
  created:
    - native/vendor/charls/.gitkeep
    - native/vendor/ffmpeg/.gitkeep
    - native/src/jls_wrapper.h
    - native/src/jls_wrapper.c
    - native/src/video_wrapper.h
    - native/src/video_wrapper.c
  modified:
    - native/vendor/README.md
    - native/src/sharpdicom_codecs.c
    - native/build.zig
    - .github/workflows/ci.yml
decisions:
  - Handle-based video decoder for multi-frame support
  - Parameter structs for encode/decode configuration
  - Stub implementations when vendor libraries not linked
  - CharLS 2.4.2 for JPEG-LS support
  - FFmpeg 7.1 for video codec support
metrics:
  duration: 9min
  completed: 2026-01-29
---

# Phase 13 Plan 04: CharLS and FFmpeg Wrappers Summary

JPEG-LS wrapper using CharLS for lossless/near-lossless encoding, and video decoder using FFmpeg for MPEG-2/H.264/HEVC multi-frame DICOM video.

## One-Liner

CharLS JPEG-LS wrapper with lossless/near-lossless support, and FFmpeg video decoder for MPEG-2/H.264/HEVC frame extraction.

## Commits

| Hash | Type | Description |
|------|------|-------------|
| 2039a61 | feat | add JPEG-LS wrapper using CharLS |
| a77a972 | feat | add video wrapper using FFmpeg |
| dae3995 | chore | update CI to download CharLS and FFmpeg sources |

## Key Changes

### JPEG-LS Wrapper (CharLS)

Created `jls_wrapper.h` and `jls_wrapper.c` providing:

- **jls_decode()**: Decode JPEG-LS to raw pixels
- **jls_encode()**: Encode raw pixels to JPEG-LS
- **jls_get_decode_size()**: Get required output buffer size
- **jls_get_encode_bound()**: Get maximum encoded size
- **jls_free()**: Free allocated buffers

Features:
- Lossless and near-lossless encoding modes
- 2-16 bits per sample precision
- 1-255 color components
- Interleave mode support (none, line, sample)
- Full CharLS 2.4.x API integration
- Approximately 2x faster than HP reference implementation

### Video Wrapper (FFmpeg)

Created `video_wrapper.h` and `video_wrapper.c` providing:

- **video_decoder_create()**: Create decoder for specified codec
- **video_decoder_get_info()**: Get stream information
- **video_decode_frame()**: Decode a video frame
- **video_decoder_flush()**: Flush buffered frames
- **video_decoder_seek()**: Reset decoder for seeking
- **video_decoder_get_frame_size()**: Get output buffer size
- **video_decoder_reset()**: Reset decoder state
- **video_decoder_destroy()**: Free decoder resources

Supported codecs:
- MPEG-2 Video (DICOM MPEG2 Main Profile)
- MPEG-4 Part 2
- H.264 / AVC (DICOM MPEG-4 AVC/H.264)
- HEVC / H.265 (DICOM MPEG-4 HEVC/H.265)

Output formats:
- GRAY8 (8-bit grayscale)
- GRAY16 (16-bit grayscale)
- RGB24 (24-bit RGB)
- YUV420P (native format, fastest)

### Build System Updates

Updated `build.zig` to:
- Detect CharLS source at `vendor/charls/src`
- Detect FFmpeg source at `vendor/ffmpeg/src`
- Compile wrappers with appropriate feature flags
- Link CharLS (libcharls) and FFmpeg (libavcodec, libavutil, libswscale)
- Add stub implementations to test executable

Updated `sharpdicom_codecs.c` to:
- Include jls_wrapper.h and video_wrapper.h
- Add SHARPDICOM_HAS_VIDEO feature flag
- Report JLS and MPEG capabilities in sharpdicom_features()

### CI Workflow Updates

Updated `.github/workflows/ci.yml` to:
- Download CharLS 2.4.2 source tarball
- Download FFmpeg n7.1 source tarball
- Extract to vendor subdirectories before build

## Deviations from Plan

None - plan executed exactly as written.

## Technical Notes

### Handle-Based Video Decoder

The video decoder uses an opaque handle pattern because:
- Video decoders maintain internal state (reference frames, B-frame reordering)
- Multi-frame DICOM videos require sequential frame decoding
- Handle allows decoder reuse across frames

### Pixel Format Conversion

The video wrapper uses libswscale for format conversion:
- YUV420P is the native format for most video codecs
- Conversion to grayscale or RGB done on-demand
- sws_context cached for repeated conversions

### Stub Implementations

When vendor libraries are not linked (local development):
- All functions return SHARPDICOM_ERR_UNSUPPORTED
- Error message indicates which library is missing
- Allows building and testing core functionality

## Verification Checklist

- [x] native/vendor/charls/.gitkeep exists
- [x] native/vendor/ffmpeg/.gitkeep exists
- [x] native/vendor/README.md documents CharLS and FFmpeg
- [x] native/src/jls_wrapper.h declares JPEG-LS functions
- [x] native/src/jls_wrapper.c implements CharLS API usage
- [x] native/src/video_wrapper.h declares video decoder functions
- [x] native/src/video_wrapper.c implements FFmpeg API usage
- [x] Handle-based API for video decoder supports multi-frame
- [x] sharpdicom_features() reports JLS and MPEG capabilities
- [x] build.zig includes both wrappers and links libraries

## Next Phase Readiness

Phase 13 Plan 05 (Managed Wrapper and NuGet Package) can proceed:
- All native wrappers implemented (JPEG, J2K, JLS, Video)
- Feature detection API available (sharpdicom_features)
- SIMD detection API available (sharpdicom_simd_features)
- Build system supports all six target platforms
- CI downloads all vendor libraries
