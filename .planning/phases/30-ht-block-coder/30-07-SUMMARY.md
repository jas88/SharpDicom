---
phase: 30
plan: 7
subsystem: codecs
tags: [progressive-decode, simd, vector256, avx2, dwt, htj2k, interface]
dependency-graph:
  requires: [30-05, 30-06]
  provides: [IProgressiveCodec, Vector256-DWT, SIMD-bit-ops]
  affects: [30-08, 30-09, 30-10]
tech-stack:
  added: []
  patterns: [runtime-simd-dispatch, progressive-decode, interface-extension]
key-files:
  created:
    - src/SharpDicom/Codecs/Htj2k/IProgressiveCodec.cs
    - tests/SharpDicom.Tests/Codecs/Htj2k/ProgressiveCodecTests.cs
    - tests/SharpDicom.Tests/Codecs/Simd/SimdHelpersExtendedTests.cs
  modified:
    - src/SharpDicom/Codecs/Simd/SimdHelpers.cs
    - src/SharpDicom/Codecs/Jpeg2000/Wavelet/Dwt53.cs
    - src/SharpDicom/Codecs/Jpeg2000/Wavelet/Dwt97.cs
    - src/SharpDicom/Codecs/Htj2k/Htj2kCodec.cs
decisions:
  - id: progressive-subsample
    description: Progressive decode uses subsample-from-full-decode rather than partial DWT
    rationale: Full DWT-level progressive decode requires architectural changes to the packet decoder; subsampling approach is correct and can be optimized later
  - id: vector256-step1-only
    description: Vector256 accelerates only Step 1 (high-pass predict) of DWT lifting
    rationale: Steps 2-4 have complex boundary dependencies making SIMD difficult without changing algorithm; Step 1 is the hot path
  - id: simd-tests-skip-gracefully
    description: Vector256 tests use Assert.Ignore when hardware unavailable
    rationale: ARM64 Macs lack AVX2; tests must not fail on CI/development machines
metrics:
  duration: 10m
  completed: 2026-02-08
  tests-before: 2806
  tests-after: 2855
  tests-added: 49
---

# Phase 30 Plan 07: IProgressiveCodec + SIMD Vector256/512 Expansion Summary

IProgressiveCodec interface for resolution-level decode; Vector256 AVX2 paths for DWT; ExtractBits/DepositBits/LeadingZeroCount/PopCount SIMD helpers.

## What Was Done

### IProgressiveCodec Interface
- Created `IProgressiveCodec` extending `IPixelDataCodec` with three methods:
  - `GetResolutionLevels`: returns decomposition levels + 1 from J2K codestream header
  - `DecodeAtResolution`: decodes at a requested resolution level (0 = thumbnail, max = full)
  - `GetResolutionDimensions`: computes output width/height for a given resolution level
- Implemented on `Htj2kCodecBase`, so all three HTJ2K codecs (Lossless, Lossless RPCL, Lossy) support it

### SimdHelpers Extended
- `IsAvx512Supported` property (Vector512.IsHardwareAccelerated)
- `IsBmi2Supported` property (Bmi2.X64.IsSupported || Bmi2.IsSupported)
- Vector256 `HorizontalSum(Vector256<int>)` and `HorizontalSum(Vector256<float>)`
- Vector256 `Clamp(Vector256<int>, int, int)`
- Vector256 `Abs(Vector256<int>)`
- `ExtractBits(ulong, ulong)` - PEXT with BMI2 hardware acceleration and scalar fallback
- `DepositBits(ulong, ulong)` - PDEP with BMI2 hardware acceleration and scalar fallback
- `LeadingZeroCount(uint)` - wraps BitOperations with netstandard2.0 scalar fallback
- `PopCount(uint)` - wraps BitOperations with netstandard2.0 scalar fallback
- All methods `[MethodImpl(AggressiveInlining)]`
- All Vector256/512/BMI2 code behind `#if NET8_0_OR_GREATER`

### DWT Vector256 Upgrade
- Added Vector256 path to `Dwt53.ForwardHorizontal` (processes 8 int samples per iteration)
- Added Vector256 path to `Dwt97.ForwardHorizontal` (processes 8 float samples per iteration)
- Runtime dispatch: Vector256.IsHardwareAccelerated -> Vector128 -> scalar
- Integer precision identical across all SIMD widths (verified by existing roundtrip tests)
- All existing Vector128 and scalar paths unchanged

### Test Coverage
- 18 ProgressiveCodecTests: resolution levels, dimensions, interface checks, error handling
- 31 SimdHelpersExtendedTests: Vector256 ops, bit manipulation, hardware detection
- Vector256 tests skip gracefully on ARM64 (no AVX2)
- Polyfills project excluded via `#if !TESTING_NETSTANDARD_POLYFILLS`

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| Subsample-from-full for progressive decode | Proper partial-DWT progressive decode requires packet-level resolution awareness in the decoder; subsample approach is correct and upgradeable |
| Vector256 only on Step 1 of lifting | Steps 2-4 have data-dependent boundary conditions; Step 1 (predict) is the main hot path |
| Assert.Ignore for Vector256 tests | ARM64 CI/dev machines lack AVX2; graceful skip prevents false failures |

## Deviations from Plan

None - plan executed exactly as written.

## Next Phase Readiness

- IProgressiveCodec interface is ready for RPCL streaming integration (30-08)
- Vector256 helpers available for HT hot path optimization (30-09, 30-10)
- ExtractBits/DepositBits ready for HT significance propagation coding
