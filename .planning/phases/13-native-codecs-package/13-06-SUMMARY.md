---
# Plan Execution Summary
phase: 13-native-codecs-package
plan: 06
subsystem: native-codecs
tags: [pinvoke, native, interop, codecs, aot]

# Dependency Graph
requires: ["13-01", "13-02", "13-03", "13-04"]
provides: ["SharpDicom.Codecs project", "P/Invoke layer", "NativeCodecs API"]
affects: ["13-07"]

# Tech Tracking
tech-stack:
  added: []
  patterns: ["LibraryImport/DllImport dual-mode", "SafeHandle for native resources", "ModuleInitializer"]

# File Tracking
key-files:
  created:
    - src/SharpDicom.Codecs/SharpDicom.Codecs.csproj
    - src/SharpDicom.Codecs/NativeCodecException.cs
    - src/SharpDicom.Codecs/NativeCodecs.cs
    - src/SharpDicom.Codecs/Interop/NativeMethods.cs
    - src/SharpDicom.Codecs/Interop/SafeHandles.cs
  modified: []

# Decisions
decisions:
  - type: implementation
    decision: "Use conditional compilation for LibraryImport (NET7+) vs DllImport"
    rationale: "LibraryImport provides source-generated marshalling for AOT, DllImport needed for netstandard2.0"

  - type: implementation
    decision: "ModuleInitializer for auto-init with AppContext switch to disable"
    rationale: "Convenience for most users while allowing opt-out for edge cases"

  - type: implementation
    decision: "SafeHandle-based resource management for native allocations"
    rationale: "Ensures proper cleanup even with exceptions, prevents resource leaks"

# Metrics
metrics:
  duration: "~6 minutes"
  completed: 2026-01-30
---

# Phase 13 Plan 06: Managed P/Invoke Layer Summary

**One-liner:** P/Invoke declarations for native codec library with auto-initialization and feature detection

## What Was Built

Created the SharpDicom.Codecs managed library that provides:

1. **SharpDicom.Codecs.csproj** - Multi-targeting project (netstandard2.0, net8.0, net9.0) with AOT/trim compatibility

2. **NativeCodecException** - Exception type with native error code categorization mapping codes -1 to -8 to meaningful categories (InvalidInput, BufferTooSmall, DecodeFailed, etc.)

3. **NativeMethods.cs** - P/Invoke declarations for all native functions:
   - Version and feature detection (sharpdicom_version, sharpdicom_features, sharpdicom_simd_features)
   - JPEG codec (jpeg_decode, jpeg_encode, jpeg_free, jpeg_decode_header)
   - JPEG 2000 codec (j2k_decode, j2k_encode, j2k_free, j2k_get_info)
   - JPEG-LS codec (jls_decode, jls_encode, jls_free, jls_get_info)
   - Video codec (video_decoder_create, video_decode_frame, video_decoder_destroy)
   - GPU acceleration (gpu_available, gpu_j2k_decode, gpu_j2k_decode_batch)

4. **SafeHandles.cs** - Safe handle types for native resource management:
   - VideoDecoderHandle - For video decoder state
   - JpegMemoryHandle, Jpeg2000MemoryHandle, JpegLsMemoryHandle - For codec-allocated memory

5. **NativeCodecs.cs** - Static API for initialization and feature detection:
   - IsAvailable, GpuAvailable, ActiveSimdFeatures properties
   - Initialize() with NativeCodecOptions
   - ModuleInitializer for auto-init on NET5+
   - DllImportResolver for custom library paths
   - Single-file deployment support via AppContext.BaseDirectory

## Commits

| Hash | Description |
|------|-------------|
| f4df953 | Create SharpDicom.Codecs project structure with AOT/trim flags and exception type |
| a43ead4 | Implement P/Invoke layer with LibraryImport/DllImport, SafeHandles, and NativeCodecs |

## Verification Checklist

- [x] SharpDicom.Codecs.csproj targets netstandard2.0, net8.0, net9.0
- [x] Project references SharpDicom core
- [x] IsAotCompatible and IsTrimmable are set (conditional on framework)
- [x] NativeCodecException provides error categorization
- [x] NativeMethods.cs has both LibraryImport (NET7+) and DllImport versions
- [x] All native functions have P/Invoke declarations
- [x] NativeCodecs.Initialize() loads library and detects features
- [x] ModuleInitializer auto-initializes on supported frameworks
- [x] DllImportResolver setup for custom library paths
- [x] Project builds on all target frameworks (verified: 0 errors, 0 warnings)
- [x] All 3404 tests pass

## Deviations from Plan

None - plan executed exactly as written.

## File Statistics

| File | Lines | Purpose |
|------|-------|---------|
| NativeCodecs.cs | 668 | Initialization and feature detection |
| NativeMethods.cs | 498 | P/Invoke declarations |
| SafeHandles.cs | 164 | Native resource management |
| NativeCodecException.cs | 222 | Exception with error categorization |
| SharpDicom.Codecs.csproj | 46 | Project configuration |

## Next Phase Readiness

Plan 13-07 (native codec wrappers) can proceed. This plan provides:

1. **NativeMethods** - All P/Invoke entry points for native functions
2. **NativeCodecs.HasFeature()** - Feature detection for conditional codec registration
3. **SafeHandles** - Memory management for encode output buffers
4. **NativeCodecException** - Consistent error handling with native error context

The codec wrapper classes will call NativeMethods directly and use SafeHandles for memory returned from encode operations.
