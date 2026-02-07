---
phase: 27-extended-codec-support
plan: 08
subsystem: codecs
tags: [video, h264, hevc, mpeg2, ffmpeg, encoding, stb-image, pinvoke]

# Dependency graph
requires:
  - phase: 27-01
    provides: native codec infrastructure (NativeMethods, NativeCodecs, SafeHandles)
  - phase: 27-06
    provides: FFmpeg build infrastructure for video encoding
provides:
  - VideoEncoder high-level API with EncodeFromFrames, EncodeFromFramesAsync, EncodeFromDicom
  - NativeVideoEncoder P/Invoke wrapper for native FFmpeg encoder
  - NativeImageLoader for loading PNG/JPEG/BMP/TGA images as VideoFrames
  - VideoFrame, VideoEncoderOptions, VideoEncodeProgress supporting types
  - VideoEncoderBackend delegate pattern for backend registration
  - Frame rate detection from DICOM tags (CineRate, FrameTime, etc.)
affects: [27-09, 27-10]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - VideoEncoderBackend delegate pattern decouples core API from native implementation
    - FrameRateToRational conversion for NTSC-safe encoding
    - stb_image P/Invoke for image file loading

key-files:
  created:
    - src/SharpDicom/Codecs/Video/VideoFrame.cs
    - src/SharpDicom/Codecs/Video/VideoEncoderOptions.cs
    - src/SharpDicom/Codecs/Video/VideoEncodeProgress.cs
    - src/SharpDicom/Codecs/Video/VideoEncoder.cs
    - src/SharpDicom.Codecs/Codecs/NativeVideoEncoder.cs
    - src/SharpDicom.Codecs/Codecs/NativeImageLoader.cs
  modified:
    - src/SharpDicom.Codecs/Interop/NativeMethods.cs
    - src/SharpDicom.Codecs/Interop/SafeHandles.cs
    - src/SharpDicom.Codecs/NativeCodecs.cs

key-decisions:
  - "VideoEncoder in core project uses delegate backend pattern to avoid hard dependency on SharpDicom.Codecs"
  - "IAsyncEnumerable API gated behind NET8_0_OR_GREATER to ensure netstandard2.0 compatibility"
  - "VideoEncodeProgress is a struct (not record struct) for netstandard2.0 compatibility with manual equality"
  - "NTSC frame rates (29.97, 23.976, 59.94) handled with exact rational representation (30000/1001)"
  - "AudioSampleFormat.IeeeFloat renamed from Float32 to satisfy CA1720 analyzer rule"

patterns-established:
  - "Backend delegate pattern: high-level API in core, native impl registered at runtime"
  - "VideoEncoderConfig StructLayout.Sequential for C interop"
  - "Frame rate detection chain: CineRate > RecommendedDisplayFrameRate > FrameTime > FrameTimeVector"

# Metrics
duration: 12min
completed: 2026-02-07
---

# Phase 27 Plan 08: Video Encoding API Summary

**VideoEncoder high-level API with streaming/batch modes, NativeVideoEncoder FFmpeg wrapper, and stb_image loader for creating video DICOM files**

## Performance

- **Duration:** 12 min
- **Started:** 2026-02-07T04:32:29Z
- **Completed:** 2026-02-07T04:44:45Z
- **Tasks:** 2
- **Files modified:** 9

## Accomplishments
- Complete video encoding type system (VideoFrame, VideoEncoderOptions with Diagnostic/Review/Archive presets, VideoEncodeProgress with ETA)
- High-level VideoEncoder API with EncodeFromFrames (sync), EncodeFromFramesAsync (IAsyncEnumerable), and EncodeFromDicom (multi-frame DICOM)
- NativeVideoEncoder wrapping native FFmpeg encoder with proper SafeHandle lifecycle management
- NativeImageLoader for loading common image formats into VideoFrame objects via stb_image
- P/Invoke declarations for video_encoder_* and stbi_* functions (both LibraryImport and DllImport)
- Frame rate detection from 4 DICOM tags with NTSC-safe rational conversion

## Task Commits

Each task was committed atomically:

1. **Task 1: Create VideoFrame, VideoEncoderOptions, VideoEncodeProgress types** - `551a7bb` (feat)
2. **Task 2: Create VideoEncoder and NativeVideoEncoder** - `6d46292` (feat)

## Files Created/Modified
- `src/SharpDicom/Codecs/Video/VideoFrame.cs` - Frame pixel data container with dimensions and format validation
- `src/SharpDicom/Codecs/Video/VideoEncoderOptions.cs` - Codec selection, quality presets, frame rate, hardware accel options
- `src/SharpDicom/Codecs/Video/VideoEncodeProgress.cs` - Encoding progress with percentage, ETA, elapsed time
- `src/SharpDicom/Codecs/Video/VideoEncoder.cs` - High-level static API with backend delegate pattern
- `src/SharpDicom.Codecs/Codecs/NativeVideoEncoder.cs` - Managed FFmpeg encoder wrapper with frame-by-frame API
- `src/SharpDicom.Codecs/Codecs/NativeImageLoader.cs` - stb_image loader producing VideoFrame objects
- `src/SharpDicom.Codecs/Interop/NativeMethods.cs` - P/Invoke for video encoder and stb_image functions
- `src/SharpDicom.Codecs/Interop/SafeHandles.cs` - VideoEncoderHandle SafeHandle
- `src/SharpDicom.Codecs/NativeCodecs.cs` - VideoEncoder and StbImage feature detection

## Decisions Made
- VideoEncoder placed in SharpDicom core (not SharpDicom.Codecs) using delegate backend pattern to avoid forcing all users to reference the native library
- IAsyncEnumerable overload only available on NET8_0_OR_GREATER since it requires the runtime support
- VideoEncodeProgress implemented as a plain struct with manual IEquatable rather than record struct for netstandard2.0 compatibility
- Frame rate rational conversion handles NTSC rates exactly (30000/1001) to avoid drift
- VideoEncoderConfig struct uses StructLayout.Sequential for direct C interop without marshalling overhead

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] CA1720 analyzer: Float32 enum name contains type name**
- **Found during:** Task 1 (VideoFrame.cs)
- **Issue:** CA1720 code analysis rule flags enum members containing type names
- **Fix:** Renamed `AudioSampleFormat.Float32` to `AudioSampleFormat.IeeeFloat`
- **Files modified:** src/SharpDicom/Codecs/Video/VideoFrame.cs
- **Committed in:** 551a7bb (Task 1 commit)

**2. [Rule 3 - Blocking] CA1510/CA1513: Must use ThrowIfNull/ThrowIf helpers**
- **Found during:** Task 2 (VideoEncoder.cs, NativeVideoEncoder.cs, NativeImageLoader.cs)
- **Issue:** TreatWarningsAsErrors with latest AnalysisLevel requires using framework throw helpers
- **Fix:** Replaced manual null checks with ThrowHelpers.ThrowIfNull and ThrowHelpers.ThrowIfDisposed
- **Files modified:** VideoEncoder.cs, NativeVideoEncoder.cs, NativeImageLoader.cs
- **Committed in:** 6d46292 (Task 2 commit)

**3. [Rule 3 - Blocking] CS8602: Nullable dereference on netstandard2.0**
- **Found during:** Task 2 (VideoEncoder.cs)
- **Issue:** string.IsNullOrEmpty lacks [NotNullWhen(false)] on netstandard2.0
- **Fix:** Added null-forgiving operator on frameTimeVectorStr
- **Files modified:** src/SharpDicom/Codecs/Video/VideoEncoder.cs
- **Committed in:** 6d46292 (Task 2 commit)

**4. [Rule 1 - Bug] PixelDataInfo uses nullable properties in Data namespace**
- **Found during:** Task 2 (VideoEncoder.cs EncodeFromDicom)
- **Issue:** Plan assumed non-nullable Rows/Columns/etc., but Data.PixelDataInfo uses nullable ushort?
- **Fix:** Added null checks with ArgumentException for missing dimension tags
- **Files modified:** src/SharpDicom/Codecs/Video/VideoEncoder.cs
- **Committed in:** 6d46292 (Task 2 commit)

---

**Total deviations:** 4 auto-fixed (2 bugs, 2 blocking)
**Impact on plan:** All auto-fixes necessary for correct compilation. No scope creep.

## Issues Encountered
None beyond the auto-fixed compilation issues.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Video encoding API complete and callable from both core and native codec packages
- Ready for Plan 09 (integration tests / end-to-end video encoding tests)
- Backend registration mechanism ready for NativeCodecs.RegisterCodecs() integration

---
*Phase: 27-extended-codec-support*
*Completed: 2026-02-07*
