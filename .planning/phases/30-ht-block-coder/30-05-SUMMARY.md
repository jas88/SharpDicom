---
phase: 30-ht-block-coder
plan: 05
subsystem: codecs
tags: [ht-block-coder, sigprop, magref, jpeg2000, htj2k, iblock-coder, refinement-passes]

# Dependency graph
requires:
  - phase: 30-02
    provides: IBlockCoder interface, CodeBlockData struct, EbcotBlockCoder
  - phase: 30-04
    provides: HtCleanup static class with Encode/Decode methods
provides:
  - HtSigProp significance propagation refinement pass
  - HtMagRef magnitude refinement pass
  - HtBlockEncoder implementing IBlockCoder for HTJ2K
  - HtBlockDecoder standalone decoder class
  - 1/3/6 pass adaptive encoding based on MSB position
affects: [30-06, 30-07, 30-08, 30-09, 30-10]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - byte-aligned bitstream with 4-byte bit-count prefix (SigProp/MagRef)
    - embedded pass-length header for self-describing multi-pass data
    - singleton pattern for stateless block coder
    - significance state derivation via cleanup decode + non-zero check

# File tracking
key-files:
  created:
    - src/SharpDicom/Codecs/Jpeg2000/Tier1/HtSigProp.cs
    - src/SharpDicom/Codecs/Jpeg2000/Tier1/HtMagRef.cs
    - src/SharpDicom/Codecs/Jpeg2000/Tier1/HtBlockEncoder.cs
    - src/SharpDicom/Codecs/Jpeg2000/Tier1/HtBlockDecoder.cs
    - tests/SharpDicom.Tests/Codecs/Jpeg2000/Tier1/HtBlockCoderTests.cs
  modified: []

# Decisions
decisions:
  - id: D30-05-01
    title: Significance state derived from cleanup decode
    context: HtCleanup.Encode/Decode don't expose significance state
    decision: Run HtCleanup.Decode on encoded data, derive sigState from non-zero coefficients
    alternatives: Modify HtCleanup API to expose sigState
    rationale: Avoids modifying existing stable HtCleanup API; minimal overhead for decode-then-check
  - id: D30-05-02
    title: Byte-aligned bitstream for SigProp/MagRef
    context: Could use VLC/MEL/MagSgn three-stream format or simple bitstream
    decision: Simple byte-aligned bitstream with 4-byte bit-count prefix
    alternatives: Three-stream format matching cleanup pass
    rationale: Self-consistent roundtrip is primary goal; simpler format reduces bugs
  - id: D30-05-03
    title: Embedded pass-length header for multi-pass data
    context: IBlockCoder.DecodeBlock receives data+numPasses but not PassLengths
    decision: When numPasses > 1, prefix data with cumulative pass lengths (4 bytes each, LE)
    alternatives: Modify IBlockCoder interface to pass PassLengths
    rationale: Keeps IBlockCoder interface unchanged; self-describing format
  - id: D30-05-04
    title: Adaptive pass count based on MSB position
    context: Need to decide how many passes to produce
    decision: MSB=0 -> 1 pass; MSB=1 -> 3 passes; MSB>=2 -> 6 passes
    alternatives: Always produce max passes, or make configurable
    rationale: Matches data precision needs; cleanup alone is lossless for full precision

# Metrics
metrics:
  duration: ~9 minutes
  completed: 2026-02-08
---

# Phase 30 Plan 05: HtSigProp + HtMagRef + HtBlockEncoder/HtBlockDecoder Summary

HtSigProp and HtMagRef refinement passes plus HtBlockEncoder/Decoder assembling all three HT passes into IBlockCoder-compatible block coding with 1/3/6 pass adaptive encoding.

## Accomplishments

### HtSigProp (Significance Propagation)
- Processes samples NOT yet significant but with significant 8-connected neighbors
- Encodes significance bit, then sign + full magnitude mantissa for newly significant samples
- Byte-aligned bitstream with 4-byte little-endian bit-count prefix
- `Encode(coefficients, sigState, width, height, subbandType, bitplane)` returns `byte[]`
- `Decode(data, coefficients, sigState, width, height, subbandType, bitplane)` updates in-place
- AggressiveInlining on inner loops, stackalloc for small buffers, ArrayPool for large

### HtMagRef (Magnitude Refinement)
- Processes samples that ARE already significant
- Encodes exactly one magnitude bit per significant sample at specified bitplane
- Same byte-aligned bitstream format as SigProp
- `Encode(coefficients, sigState, width, height, bitplane)` returns `byte[]`
- `Decode(data, coefficients, sigState, width, height, bitplane)` OR's refinement bits

### HtBlockEncoder (IBlockCoder)
- Implements `IBlockCoder` interface for HTJ2K pipeline integration
- Static `Instance` singleton (stateless, safe for concurrent use)
- Adaptive pass count: 1 pass (MSB=0), 3 passes (MSB=1), 6 passes (MSB>=2)
- Multi-pass data format: embedded cumulative pass-length header + concatenated passes
- Derives significance state by decoding cleanup output and checking for non-zero values
- HT Set 1: Cleanup + SigProp(bp=0) + MagRef(bp=0)
- HT Set 2: SigProp(bp=1) + MagRef(bp=1) (for 6-pass mode)

### HtBlockDecoder
- Standalone decoder class with `Instance` singleton
- Delegates to `HtBlockEncoder.Instance.DecodeBlock` for implementation
- Parses embedded pass-length header for multi-pass data

### Test Suite (38 tests)
- Cleanup-only roundtrip (1 pass) with +/-1 values
- Full quality roundtrip (3 passes, 1 HT Set)
- Two HT Sets roundtrip (6 passes)
- IBlockCoder interface conformance (singleton, type check)
- Various code-block sizes: 4x4, 16x16, 32x32, 64x64
- All subband types: LL (0), LH (1), HL (2), HH (3)
- Edge cases: all-zero, single non-zero, maximum magnitude
- Pass count validation for MSB positions 0, 1, 2+
- PassLengths monotonicity and total length consistency
- Random data roundtrip with 5 seeds (mixed zero/small/large)
- FsCheck property test: random coefficients roundtrip losslessly
- Deterministic encode/decode verification
- Odd dimension roundtrip (5x5, 7x3, 3x7, 9x5)

## Commits

| Hash | Type | Description |
|------|------|-------------|
| cddfb58 | feat | HtSigProp and HtMagRef refinement passes |
| a09427a | feat | HtBlockEncoder and HtBlockDecoder with IBlockCoder |
| 313a058 | test | Comprehensive HtBlockCoder roundtrip tests |

## Decisions Made

1. **Significance state via cleanup decode** (D30-05-01): Rather than modifying HtCleanup's API to expose significance state, we decode the cleanup output and derive significance from non-zero coefficients. This preserves HtCleanup's stable API.

2. **Byte-aligned bitstream format** (D30-05-02): SigProp and MagRef use a simple byte-aligned bitstream with a 4-byte bit-count prefix, rather than the three-stream VLC/MEL/MagSgn format. This keeps the refinement passes straightforward.

3. **Embedded pass-length header** (D30-05-03): Since IBlockCoder.DecodeBlock only receives raw data + numPasses (not PassLengths), we embed cumulative pass lengths at the start of multi-pass data. Single-pass data remains headerless (raw cleanup segment).

4. **Adaptive pass count** (D30-05-04): Pass count scales with data precision needs: MSB=0 (only +/-1 values) produces 1 pass; MSB=1 produces 3 passes; MSB>=2 produces 6 passes.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] CA1822 analyzer error on HtBlockDecoder.DecodeBlock**
- **Found during:** Task 2
- **Issue:** Instance method without instance data triggers CA1822 (TreatWarningsAsErrors)
- **Fix:** Added #pragma warning disable/restore CA1822 with comment explaining the instance method is intentional for API symmetry
- **Files modified:** HtBlockDecoder.cs
- **Commit:** a09427a

**2. [Rule 3 - Blocking] FsCheck.Random vs System.Random ambiguity**
- **Found during:** Task 2 (test compilation)
- **Issue:** `using FsCheck` imports `FsCheck.Random` which conflicts with `System.Random`
- **Fix:** Added `using Random = System.Random;` alias
- **Files modified:** HtBlockCoderTests.cs
- **Commit:** 313a058

## Test Results

- **Before:** 2744 tests (2689 pass, 55 skipped, 0 fail)
- **After:** 2782 tests (2727 pass, 55 skipped, 0 fail)
- **Added:** 38 new tests, all passing
- **Regressions:** None

## Next Phase Readiness

Plan 30-05 provides the complete HT block coding pipeline:
- `HtBlockEncoder.Instance` is a drop-in replacement for `EbcotBlockCoder.Instance`
- Both implement `IBlockCoder` and can be selected per transfer syntax
- Plans 30-06 through 30-10 can build on this foundation for:
  - Standard VLC table integration (30-06)
  - Pipeline integration with tier-2 (30-07+)
  - Performance optimization (30-09)
  - Conformance testing (30-10)
