---
phase: 13-native-codecs-package
plan: 06
subsystem: codecs
tags: [pinvoke, interop, native, jpeg, jpeg2000, jpegls, aot, trim]

# Dependency graph
requires:
  - phase: 13-01
    provides: Native C wrapper API design
  - phase: 13-02
    provides: libjpeg-turbo Zig wrapper
  - phase: 13-03
    provides: OpenJPEG/CharLS Zig wrappers
  - phase: 13-04
    provides: JPEG 2000/JPEG-LS native implementation
provides:
  - SharpDicom.Codecs managed project
  - P/Invoke declarations for all native functions
  - NativeCodecs static initialization class
  - Feature detection (SIMD, GPU)
  - NativeCodecException with error categorization
affects: [13-07, 13-08, 13-09]

# Tech tracking
tech-stack:
  added:
    - P/Invoke with LibraryImport (NET7+)
    - DllImport fallback (netstandard2.0)
  patterns:
    - Dual P/Invoke declarations for AOT compatibility
    - ModuleInitializer for auto-initialization
    - DllImportResolver for custom library paths
    - SafeHandle for native resource cleanup

key-files:
  created:
    - src/SharpDicom.Codecs/SharpDicom.Codecs.csproj
    - src/SharpDicom.Codecs/NativeCodecException.cs
    - src/SharpDicom.Codecs/Interop/NativeMethods.cs
    - src/SharpDicom.Codecs/Interop/SafeHandles.cs
    - src/SharpDicom.Codecs/NativeCodecs.cs
  modified:
    - SharpDicom.sln

key-decisions:
  - "Use LibraryImport on NET7+ with DllImport fallback for AOT compatibility"
  - "ModuleInitializer for automatic initialization with opt-out via AppContext switch"
  - "AppContext.BaseDirectory for single-file app compatibility"
  - "SafeHandle for VideoDecoder and native buffer cleanup"

patterns-established:
  - "Dual P/Invoke pattern: LibraryImport for modern, DllImport for legacy"
  - "Feature detection via native library version and feature bitmasks"
  - "Error categorization via NativeCodecErrorCategory enum"

# Metrics
duration: 10min
completed: 2026-01-31
---

# Phase 13 Plan 06: Managed P/Invoke Layer Summary

**P/Invoke interop layer with LibraryImport/DllImport dual declarations, NativeCodecs initialization, and feature detection**

## Performance

- **Duration:** 10 min
- **Started:** 2026-01-31T20:34:28Z
- **Completed:** 2026-01-31T20:44:42Z
- **Tasks:** 2
- **Files modified:** 6

## Accomplishments
- Created SharpDicom.Codecs project with AOT/trim compatibility
- Implemented P/Invoke declarations for all native codec functions
- Built NativeCodecs static class with auto-initialization and feature detection
- Added SafeHandle implementations for resource cleanup

## Task Commits

Each task was committed atomically:

1. **Task 1: Create SharpDicom.Codecs project structure** - `2939e82` (feat)
2. **Task 2: Implement P/Invoke layer and NativeCodecs** - `d7cac10` (feat)

## Files Created/Modified
- `src/SharpDicom.Codecs/SharpDicom.Codecs.csproj` - Project with multi-targeting and AOT flags
- `src/SharpDicom.Codecs/NativeCodecException.cs` - Exception with native error categorization
- `src/SharpDicom.Codecs/Interop/NativeMethods.cs` - P/Invoke declarations (LibraryImport + DllImport)
- `src/SharpDicom.Codecs/Interop/SafeHandles.cs` - VideoDecoderHandle and NativeBufferHandle
- `src/SharpDicom.Codecs/NativeCodecs.cs` - Static initialization and feature detection
- `SharpDicom.sln` - Added project reference

## Decisions Made
- Used LibraryImport on NET7+ for source-generated P/Invoke with DllImport fallback for older runtimes
- Implemented ModuleInitializer for auto-initialization with AppContext switch to disable
- Used AppContext.BaseDirectory instead of Assembly.Location for single-file app compatibility
- Created SafeHandle types for proper native resource cleanup

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- CA2255 warning about ModuleInitializer usage - resolved with pragma disable
- IL3000 warning about Assembly.Location in single-file apps - switched to AppContext.BaseDirectory
- CS1574 cref resolution failure on netstandard2.0 - removed cref to ModuleInitializerAttribute

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- P/Invoke layer complete and ready for codec implementations in Plan 07
- NativeCodecs.RegisterCodecs() stub ready for codec registration
- All target frameworks (netstandard2.0, net8.0, net9.0) building successfully

---
*Phase: 13-native-codecs-package*
*Completed: 2026-01-31*
