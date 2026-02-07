---
phase: 27-extended-codec-support
plan: 11
status: complete
subsystem: codecs
tags: [video, encoder, native, registration]
requires: [27-08, 27-09, 27-10]
provides: [video-encoder-backend-registration]
affects: [video-dicom-builder-pipeline]
tech-stack:
  patterns: [backend-delegate-pattern, native-interop]
key-files:
  modified: [src/SharpDicom.Codecs/NativeCodecs.cs]
metrics:
  duration: ~3 minutes
  completed: 2026-02-07
---

# Plan 27-11: Wire VideoEncoder Backend Registration

Replaced commented-out video codec registration stub in NativeCodecs.RegisterCodecs() with a working VideoEncoder.RegisterBackend() call that delegates to NativeVideoEncoder for frame-by-frame FFmpeg-based video encoding.

## Changes Made

- Added `using SharpDicom.Codecs.Video;` to NativeCodecs.cs for access to VideoEncoder and related types
- Replaced the 5-line commented-out placeholder (`// Video codec registration - to be implemented in future plan`) with a working 9-line VideoEncoder.RegisterBackend() call
- The backend lambda creates a NativeVideoEncoder, iterates frames calling EncodeFrame(), flushes, and returns the output bitstream
- Registration is gated on `HasFeature(NativeCodecFeature.VideoEncoder)` which checks both native library support and user enable flag

## Verification

- SharpDicom.Codecs project: **Build succeeded, 0 warnings, 0 errors**
- Full solution: **Build succeeded, 0 warnings, 0 errors**
- All tests: **4844 total, 0 failed, 4661 succeeded, 183 skipped**

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| Use `NativeCodecFeature.VideoEncoder` (not `Video`) as gate | VideoEncoder feature specifically checks NativeFeatures.VideoEnc flag, matching encoder capability |
| Lambda captures no state | Encoder is created fresh per call via `using var`, no shared mutable state |

## Gaps Closed

- Gap 1: VideoEncoder backend not registered -- CLOSED (RegisterBackend wired to NativeVideoEncoder)
- Gap 2: VideoDicomBuilder depends on gap 1 -- CLOSED (VideoDicomBuilder.Build() calls VideoEncoder.EncodeFromFrames which now has a backend)

## Deviations from Plan

None - plan executed exactly as written.
