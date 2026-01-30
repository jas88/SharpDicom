---
phase: 13-native-codecs-package
plan: 05
subsystem: native-codecs
tags: [cuda, nvjpeg2000, gpu-acceleration, dynamic-loading, fallback]

# Dependency graph
requires:
  - phase: 13-01
    provides: Zig build system and C API header
  - phase: 13-03
    provides: j2k_decode CPU function for fallback
provides:
  - nvJPEG2000 wrapper for GPU-accelerated JPEG 2000 decode
  - GPU dispatch wrapper with automatic CPU fallback
  - CMake build for CUDA-based components
  - Batch decode API for multi-frame efficiency
affects: [managed-interop (NativeCodecs class), codec-registration]

# Tech tracking
tech-stack:
  added: [CUDA 11.0+, nvJPEG2000, CMake 3.18+]
  patterns: [dynamic-library-loading, gpu-fallback-chain, thread-local-preference]

key-files:
  created:
    - native/cuda/nvjpeg2k_wrapper.h
    - native/cuda/nvjpeg2k_wrapper.c
    - native/cuda/CMakeLists.txt
    - native/cuda/test_nvjpeg2k_wrapper.c
    - native/src/gpu_wrapper.h
    - native/src/gpu_wrapper.c
  modified:
    - native/src/sharpdicom_codecs.h
    - native/src/sharpdicom_codecs.c
    - native/build.zig
    - .github/workflows/ci.yml

key-decisions:
  - "Compute capability 5.0+ required (Maxwell/GTX 750 Ti and newer)"
  - "Dynamic loading of nvJPEG2000 wrapper via dlopen/LoadLibrary"
  - "Thread-local prefer_cpu flag for testing fallback behavior"
  - "CUDA wrapper built separately with nvcc, loaded at runtime"
  - "Batch decode for efficient multi-frame GPU processing"

patterns-established:
  - "GPU availability detection at runtime"
  - "Automatic fallback chain: nvJPEG2000 -> CPU j2k_decode"
  - "CUDA architectures: 50;60;70;75;80;86;89;90"
  - "Optional CI job with continue-on-error for GPU builds"

# Metrics
duration: 8min
completed: 2026-01-30
---

# Phase 13 Plan 05: GPU Acceleration (nvJPEG2000) Summary

**GPU-accelerated JPEG 2000 decode using nvJPEG2000 with automatic CPU fallback**

## Performance

- **Duration:** 8 min
- **Started:** 2026-01-30T02:35:00Z
- **Completed:** 2026-01-30T02:43:00Z
- **Tasks:** 2
- **Files created:** 6
- **Files modified:** 4

## Accomplishments

- Created nvJPEG2000 wrapper (599 lines) with CUDA integration
  - Handles init, shutdown, single-frame decode, batch decode
  - Device info query (name, compute capability, memory)
  - Thread-safe initialization with mutex/critical section
- Created GPU dispatch wrapper (525 lines) with dynamic loading
  - Searches for nvjpeg2k_wrapper library at runtime
  - Falls back to CPU j2k_decode() when GPU unavailable
  - Thread-local prefer_cpu flag for testing
  - Batch decode passthrough for multi-frame efficiency
- Added CMake build for CUDA wrapper
  - Targets CUDA 11.0+ with compute capabilities 5.0-9.0
  - Security hardening flags (-fstack-protector-strong, -D_FORTIFY_SOURCE=2)
  - SONAME versioning for library compatibility
- Updated CI workflow with cuda-build job
  - Uses nvidia/cuda:12.3.2-devel-ubuntu22.04 container
  - Checks for nvJPEG2000 availability
  - continue-on-error: true (GPU builds are optional)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create nvJPEG2000 wrapper (CUDA)** - `00a2c88` (feat)
   - native/cuda/nvjpeg2k_wrapper.h (226 lines)
   - native/cuda/nvjpeg2k_wrapper.c (599 lines)
   - native/cuda/CMakeLists.txt (98 lines)
   - native/cuda/test_nvjpeg2k_wrapper.c (147 lines)
   - .github/workflows/ci.yml (cuda-build job)

2. **Task 2: Create GPU dispatch wrapper** - integrated with 13-03
   - native/src/gpu_wrapper.h (179 lines)
   - native/src/gpu_wrapper.c (525 lines)
   - native/src/sharpdicom_codecs.h (GPU exports)
   - native/src/sharpdicom_codecs.c (GPU dispatch functions)
   - native/build.zig (gpu_wrapper.c, -ldl)

## Files Created/Modified

**Created:**
- `native/cuda/nvjpeg2k_wrapper.h` - nvJPEG2000 C API declarations
- `native/cuda/nvjpeg2k_wrapper.c` - CUDA/nvJPEG2000 implementation
- `native/cuda/CMakeLists.txt` - CMake build for CUDA wrapper
- `native/cuda/test_nvjpeg2k_wrapper.c` - Test executable for wrapper
- `native/src/gpu_wrapper.h` - GPU dispatch API declarations
- `native/src/gpu_wrapper.c` - Dynamic loading and CPU fallback

**Modified:**
- `native/src/sharpdicom_codecs.h` - Added GPU API exports
- `native/src/sharpdicom_codecs.c` - Added GPU dispatch functions
- `native/build.zig` - Added gpu_wrapper.c and -ldl link
- `.github/workflows/ci.yml` - Added cuda-build job

## Decisions Made

- **Compute 5.0+ minimum**: Maxwell architecture (GTX 750 Ti, 2014) is oldest supported
- **Dynamic loading**: nvJPEG2000 wrapper loaded at runtime to avoid CUDA dependency
- **Thread-local preference**: gpu_prefer_cpu() enables testing fallback without GPU
- **Batch API**: Enables efficient multi-frame decode on GPU
- **Optional CI job**: CUDA builds don't block CI; nvJPEG2000 may not be in all containers

## Deviations from Plan

None - plan executed as specified.

## Issues Encountered

None - implementation followed plan exactly.

## User Setup Required

For GPU acceleration to work:
1. NVIDIA GPU with compute capability 5.0+ (Maxwell or newer)
2. CUDA Toolkit 11.0+ installed
3. nvJPEG2000 library available (bundled with CUDA or separate download)
4. nvjpeg2k_wrapper library in search path or same directory

## Next Phase Readiness

- GPU acceleration ready for managed interop layer
- NativeCodecs class can call sharpdicom_gpu_available() and sharpdicom_gpu_j2k_decode()
- CPU fallback ensures graceful degradation on non-GPU systems
- Batch API enables high-throughput multi-frame decode

---
*Phase: 13-native-codecs-package*
*Completed: 2026-01-30*
