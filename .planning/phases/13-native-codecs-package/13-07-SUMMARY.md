---
# Execution metadata
phase: 13-native-codecs-package
plan: 07
subsystem: codecs

# Tags for discoverability
tags:
  - native-codecs
  - jpeg
  - jpeg2000
  - jpeg-ls
  - p/invoke
  - codec-registry
  - priority

# Dependency graph
requires:
  - 13-06 # P/Invoke layer
provides:
  - native-codec-implementations
  - priority-based-codec-registration
affects:
  - 13-08 # NuGet packaging

# Tech tracking
tech-stack:
  added: []
  patterns:
    - priority-based-registration
    - native-wrapper-pattern
    - gpu-fallback

# Files changed
key-files:
  created:
    - src/SharpDicom.Codecs/Codecs/NativeJpegCodec.cs
    - src/SharpDicom.Codecs/Codecs/NativeJpeg2000Codec.cs
    - src/SharpDicom.Codecs/Codecs/NativeJpegLsCodec.cs
  modified:
    - src/SharpDicom/Codecs/CodecRegistry.cs
    - src/SharpDicom.Codecs/NativeCodecs.cs

# Decisions made this plan
decisions:
  - id: priority-registry
    choice: "Higher priority replaces, equal priority replaces (last wins), lower priority ignored"
    context: "Need native codecs to override pure C# codecs"
    impact: "DefaultPriority=50, NativePriority=100"
  - id: gpu-fallback
    choice: "Automatic GPU to CPU fallback for J2K"
    context: "GPU may not be available or may fail"
    impact: "Seamless degradation to OpenJPEG"

# Metrics
metrics:
  duration: 8m
  completed: 2026-01-31
---

# Phase 13 Plan 07: IPixelDataCodec Wrapper Implementation Summary

Priority-based CodecRegistry with native JPEG/J2K/JLS codec wrappers at priority 100

## One-liner

Native codec wrappers implementing IPixelDataCodec with P/Invoke, GPU fallback for J2K, and priority-based registration

## What Was Built

### Task 1: Enhanced CodecRegistry with Priority Support

Added priority-based registration to CodecRegistry:

- `Register(codec, priority)` overload for priority-based registration
- `DefaultPriority` (50) for pure C# codecs
- `NativePriority` (100) for native codecs
- `GetCodecInfo()` and `GetPriority()` for debugging
- Equal priority replaces existing (last wins), lower priority is ignored

### Task 2: Native Codec Wrappers

Implemented three IPixelDataCodec implementations wrapping P/Invoke calls:

**NativeJpegCodec** (322 lines)
- Wraps libjpeg-turbo via P/Invoke
- Supports JPEG Baseline (lossy)
- Options: quality (1-100), subsampling (4:4:4, 4:2:2, 4:2:0)
- Full encode/decode/validate implementation

**NativeJpeg2000Codec** (369 lines)
- Wraps OpenJPEG with optional nvJPEG2000 GPU support
- Automatic GPU-to-CPU fallback
- Supports lossless and lossy modes
- Options: compression ratio, tile size, resolution levels

**NativeJpegLsCodec** (353 lines)
- Wraps CharLS for JPEG-LS
- Supports lossless and near-lossless modes
- Options: NEAR parameter (0=lossless, >0=max error)

**NativeCodecs.RegisterCodecs()** updated:
- Registers all enabled codecs at priority 100
- Checks feature flags and codec availability
- Integrates with module initializer

## Key Technical Details

### Priority-Based Registration

```csharp
// Pure C# codec (default priority 50)
CodecRegistry.Register(new JpegLosslessCodec());

// Native codec (priority 100 - wins)
CodecRegistry.Register(NativeJpegCodec.CreateBaseline(), CodecRegistry.NativePriority);
```

### GPU Fallback Pattern

```csharp
// NativeJpeg2000Codec.Decode
if (GpuEnabled)
{
    result = NativeMethods.GpuJ2kDecode(...);
    if (result >= 0) return ValidateAndReturn(...);
    // GPU failed - fall through to CPU
}
// CPU decode via OpenJPEG
result = NativeMethods.J2kDecode(...);
```

### Codec Options Classes

Each codec has an options class for encode configuration:
- `NativeJpegCodecOptions` - Quality, Subsampling
- `NativeJpeg2000CodecOptions` - Lossless, CompressionRatio, TileSize
- `NativeJpegLsCodecOptions` - NearLossless, Interleaved

## Deviations from Plan

None - plan executed exactly as written.

## Test Results

All 1639 tests passing (1620 succeeded, 19 skipped - environment-dependent).

## Files Changed

| File | Lines | Change |
|------|-------|--------|
| CodecRegistry.cs | +104 | Priority support |
| NativeJpegCodec.cs | 322 | New codec wrapper |
| NativeJpeg2000Codec.cs | 369 | New codec wrapper |
| NativeJpegLsCodec.cs | 353 | New codec wrapper |
| NativeCodecs.cs | +18 | Registration implementation |

## Commits

1. `8982f88` - feat(13-07): add priority-based codec registration to CodecRegistry
2. `7206e15` - feat(13-07): implement native codec wrappers for JPEG, JPEG 2000, JPEG-LS

## Next Phase Readiness

**Blockers**: None

**Ready for Plan 08**: NuGet packaging with runtime packages

**Dependencies satisfied**:
- P/Invoke layer (Plan 06)
- Codec wrappers (this plan)
- Native library build (Plan 02-05)
