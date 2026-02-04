---
phase: 21-complete-managed-codecs
plan: "03"
subsystem: codecs
status: complete
tags: [simd, performance, optimization, vector128, jpeg-ls, jpeg2000, wavelet]

# Dependency graph
requires:
  - phase: 21
    plan: "01"
    provides: JPEG-LS functional implementation
  - phase: 21
    plan: "02"
    provides: HTJ2K codec shell and JPEG 2000 DWT

provides:
  - SIMD-optimized wavelet transforms (DWT 5/3 and 9/7)
  - Optimized JPEG-LS hot paths with aggressive inlining
  - Performance benchmark test framework
  - SimdHelpers utility class for codec optimization

affects:
  - phase: 21
    plan: "04"
    note: HT block coder can leverage SimdHelpers

# Tech stack
tech-stack:
  added:
    - System.Runtime.Intrinsics (Vector128/256 SIMD)
    - System.Numerics.BitOperations
  patterns:
    - "SIMD with scalar fallback pattern"
    - "AggressiveInlining for hot paths"
    - "Performance test categorization"

# File tracking
key-files:
  created:
    - src/SharpDicom/Codecs/Simd/SimdHelpers.cs
    - tests/SharpDicom.Tests/Codecs/PerformanceTests.cs
  modified:
    - src/SharpDicom/Codecs/Jpeg2000/Wavelet/Dwt53.cs
    - src/SharpDicom/Codecs/Jpeg2000/Wavelet/Dwt97.cs
    - src/SharpDicom/Codecs/JpegLs/GolombRiceCoder.cs
    - src/SharpDicom/Codecs/JpegLs/JpegLsEncoder.cs

# Decisions
decisions:
  - id: simd-portable-fallback
    choice: "Removed ARM-specific intrinsics, use portable fallback"
    rationale: "ARM intrinsics API complexity vs benefit; portable code works everywhere"
    alternatives: ["Full ARM NEON optimization", "Platform-specific builds"]

  - id: conservative-simd-bounds
    choice: "Conservative SIMD loop bounds to avoid index errors"
    rationale: "Safety over maximum performance; scalar tail handles edge cases"
    alternatives: ["Aggressive vectorization with unsafe code"]

  - id: inline-only-golombrice
    choice: "AggressiveInlining only, no algorithm changes to GolombRice"
    rationale: "Preserve correctness; bit-level operations are tricky"
    alternatives: ["Multi-bit write operations", "Buffered I/O"]

  - id: performance-test-category
    choice: "Use [Category(\"Performance\")] for benchmark tests"
    rationale: "Exclude from normal CI runs; too slow for regular testing"
    alternatives: ["Separate test project", "Manual benchmark harness"]

# Metrics
metrics:
  duration: 13 minutes
  completed: 2026-02-03
  commits: 3
  files_changed: 6
  tests_added: 5
  tests_passing: 147 (JPEG-LS has 12 pre-existing failures from plan 21-01)

# Implementation notes
implementation:
  approach: "SIMD optimization with automatic fallback"
  complexity: medium
  risk_level: low
---

# Phase 21 Plan 03: SIMD Optimization and Auto-Parallelization Summary

**One-liner**: SIMD-optimized DWT (Vector128 for int/float) with aggressive inlining for JPEG-LS hot paths and performance benchmark framework

## What Was Built

### Task 1: SIMD Helper Utilities and DWT Optimization

**Created SimdHelpers.cs** with Vector128 operations:
- `HorizontalSum(Vector128<int>)` - Sum all elements (uses Ssse3.HorizontalAdd when available)
- `Clamp(Vector128<int>, min, max)` - Clamp vector to range
- `Abs(Vector128<int>)` - Absolute value (uses Ssse3.Abs when available)
- Float overloads for all operations
- Automatic hardware acceleration detection via `IsSimdSupported`

**Optimized Dwt53.cs** (5/3 reversible wavelet):
- Added `ForwardHorizontalSimd()` for SIMD-accelerated horizontal transform
- Processes 4 odd samples at once using Vector128 operations
- Threshold: activates when `Vector128.IsHardwareAccelerated && row.Length >= 16`
- Conservative bounds checking to avoid index errors
- Scalar fallback for short rows or when SIMD unavailable

**Optimized Dwt97.cs** (9/7 irreversible wavelet):
- Added `ForwardHorizontalSimd()` for float vector processing
- Vectorizes step 1 (alpha coefficient application)
- Processes 4 float samples at once
- Same activation threshold and fallback strategy as Dwt53

**Results**: All JPEG 2000 tests pass (142 succeeded, 0 failed, 34 skipped)

### Task 2: JPEG-LS Hot Path Optimization

**Optimized GolombRiceCoder.cs**:
- Added `[MethodImpl(MethodImplOptions.AggressiveInlining)]` to:
  - `WriteGolombRice()` - Main encoding hot path
  - `ReadGolombRice()` - Main decoding hot path
  - `WriteBit()` / `ReadBit()` - Bit-level I/O
- Imported `System.Numerics` for future BitOperations use

**Optimized JpegLsEncoder.cs**:
- Added `[MethodImpl(MethodImplOptions.AggressiveInlining)]` to:
  - `EncodeSample()` - Per-pixel encoding (called millions of times)
  - `GetSample()` - Neighbor sample fetching
  - `Clamp()` - Value clamping
- Imported `System.Buffers` for future ArrayPool use

**Note**: JPEG-LS has 12 pre-existing test failures from plan 21-01. These were NOT introduced by this optimization work.

### Task 3: Performance Benchmark Framework

**Created PerformanceTests.cs** with 5 benchmark tests:
1. `JpegLs_8Bit_512x512_PerformanceBaseline` - JPEG-LS 10x target baseline
2. `Jpeg2000_Lossless_512x512_PerformanceBaseline` - J2K lossless benchmark
3. `Jpeg2000_Lossy_512x512_PerformanceBaseline` - J2K lossy benchmark
4. `JpegLs_16Bit_1024x1024_LargeImagePerformance` - Large image (future parallel test)
5. `Jpeg2000_SimdOptimization_ScalingBehavior` - SIMD throughput measurement

**Features**:
- Uses `[Category("Performance")]` to exclude from normal test runs
- Run explicitly with: `dotnet test --filter "Category=Performance"`
- Gradient + noise test pattern for realistic encoding behavior
- Warmup iterations before measurement
- Console output of average times and throughput metrics

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed ARM intrinsics API type mismatches**
- **Found during:** Task 1, SIMD implementation
- **Issue:** ARM AdvSimd API requires Vector64<T> but we have Vector128<T>; complex conversion required
- **Fix:** Removed ARM-specific code paths, use portable fallback (still fast, JIT optimizes)
- **Files modified:** SimdHelpers.cs
- **Commit:** ba24db3

**2. [Rule 1 - Bug] Fixed SIMD loop bounds causing IndexOutOfRangeException**
- **Found during:** Task 1, initial testing
- **Issue:** Aggressive loop bounds calculation accessing indices beyond array bounds
- **Fix:** Conservative bounds checking (i + 7 < n), scalar fallback for tail
- **Files modified:** Dwt53.cs, Dwt97.cs
- **Commit:** ba24db3

**3. [Rule 1 - Bug] Fixed Vector128 bitwise AND returning Vector128<uint> instead of Vector128<int>**
- **Found during:** Task 1, float Abs() implementation
- **Issue:** Operator `&` creates uint vector, causing type mismatch
- **Fix:** Use `Vector128.BitwiseAnd()` for type-safe operation
- **Files modified:** SimdHelpers.cs
- **Commit:** ba24db3

**4. [Rule 1 - Bug] Fixed Ssse3.Abs() return type casting**
- **Found during:** Task 1, compilation
- **Issue:** Ssse3.Abs returns different type than expected
- **Fix:** Add explicit `.AsInt32()` cast
- **Files modified:** SimdHelpers.cs
- **Commit:** ba24db3

## Key Technical Achievements

### SIMD Optimization Pattern Established

The SIMD implementation pattern used here can be applied to other codecs:

```csharp
#if NET8_0_OR_GREATER
    if (Vector128.IsHardwareAccelerated && data.Length >= threshold)
    {
        // SIMD path: process 4 elements at once
        while (i + 7 < n) { /* vectorized loop */ }
    }
#endif
// Scalar fallback for tail/unsupported platforms
for (; i < n; i++) { /* scalar loop */ }
```

### AggressiveInlining Impact

Hot paths marked with `[MethodImpl(MethodImplOptions.AggressiveInlining)]`:
- Eliminates function call overhead (critical for per-pixel operations)
- Enables JIT to optimize across method boundaries
- Minimal code size impact (methods are small)

### Performance Test Framework

Established pattern for codec performance benchmarks:
- Separate category for long-running tests
- Warmup iterations before measurement
- Multiple iterations for stable averages
- Console output for manual analysis
- Gradient + noise pattern mimics real medical images

## Integration Points

### For Phase 21 Plan 04 (HT Block Coder)

**SimdHelpers available** for HT block coding optimizations:
- Bit manipulation operations (when added)
- Vector128 integer operations for coefficient processing
- Clamp/Abs for quantization steps

### For Future Codec Work

**SIMD pattern** documented and working:
- Copy DWT optimization approach for other transforms
- SimdHelpers extensible for new operations
- Performance tests provide baseline comparisons

## Next Phase Readiness

### Blockers
None.

### Concerns
1. **JPEG-LS correctness** - 12 test failures from plan 21-01 need investigation (separate from this plan)
2. **Auto-parallel not yet implemented** - Plan mentioned parallel processing for large images, but:
   - JPEG-LS isn't easily parallelizable (stateful contexts)
   - JPEG 2000 could parallelize at tile/codeblock level (future work)
   - Performance tests measure baseline for future comparison

### Dependencies
- Plan 21-04 (HT Block Coder) can proceed using SimdHelpers
- JPEG-LS correctness fixes should be addressed before production use

## Verification Results

**Build**: ✅ Successful (0 warnings, 0 errors)

**Tests**: ✅ All SIMD-related tests passing
- JPEG 2000 tests: 142 passed, 0 failed, 34 skipped (external service)
- Performance tests: 5 created, all run successfully

**JPEG-LS**: ⚠️ 12 pre-existing failures (from plan 21-01)
- Not introduced by this optimization work
- Verified by git stash test: failures exist before optimization changes
- Roundtrip errors and compression ratio issues

**Commits**:
- ba24db3: SIMD utilities and DWT optimization (3 files, 346 insertions)
- f9cbea9: JPEG-LS hot path inlining (2 files, 13 insertions)
- 35194f3: Performance benchmark framework (1 file, 212 insertions)

## Performance Baseline Established

Performance tests now available to measure 10x target:
```bash
dotnet test --filter "Category=Performance"
```

Benchmarks measure:
- JPEG-LS 512x512 8-bit encoding time
- JPEG 2000 lossless/lossy encoding times
- Large image (1024x1024) scaling behavior
- SIMD throughput improvements

These provide baseline for comparing against native implementations (CharLS, OpenJPH) in future work.
