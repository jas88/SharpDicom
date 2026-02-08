---
phase: 30-ht-block-coder
plan: 06
subsystem: codecs
tags: [htj2k, ht-block-coder, codec-integration, cap-marker, encoder-options, tier2]

# Dependency graph
requires:
  - phase: 30-05
    provides: HtBlockEncoder implementing IBlockCoder, HtBlockDecoder
  - phase: 30-02
    provides: IBlockCoder interface, EbcotBlockCoder
provides:
  - HtEncoderOptions with named presets (Lossless, Diagnostic, Archive, Review, Fast)
  - CAP marker (0xFF50) parsing and generation via J2kCodestream
  - HT mode auto-detection on decode via CAP marker
  - IBlockCoder parameter on J2kEncoder.EncodeFrame and J2kDecoder.DecodeFrame
  - Tier-2 HT pass count encoding/decoding (3-bit format for 1-6 passes)
  - Full HTJ2K encode-decode pipeline using HT block coder
affects: [30-07, 30-08, 30-09, 30-10]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - IBlockCoder dispatch: encoder/decoder accept abstract block coder for algorithm selection
    - CAP marker auto-detection: decode path auto-selects HtBlockEncoder or EbcotBlockCoder
    - Named preset pattern: readonly record struct with static preset properties
    - HT mode flag propagation: isHtMode flag flows through Tier-2 packet encoding/decoding

# File tracking
key-files:
  created:
    - src/SharpDicom/Codecs/Htj2k/HtEncoderOptions.cs
    - tests/SharpDicom.Tests/Codecs/Htj2k/Htj2kIntegrationTests.cs
  modified:
    - src/SharpDicom/Codecs/Htj2k/Htj2kCodecOptions.cs
    - src/SharpDicom/Codecs/Htj2k/Htj2kCodec.cs
    - src/SharpDicom/Codecs/Jpeg2000/J2kCodestream.cs
    - src/SharpDicom/Codecs/Jpeg2000/J2kEncoder.cs
    - src/SharpDicom/Codecs/Jpeg2000/J2kDecoder.cs
    - src/SharpDicom/Codecs/Jpeg2000/Tier2/PacketEncoder.cs
    - src/SharpDicom/Codecs/Jpeg2000/Tier2/PacketDecoder.cs

# Decisions
decisions:
  - id: d30-06-01
    decision: Auto-detect HT vs EBCOT from CAP marker rather than requiring explicit block coder selection
    rationale: Simplifies API; decoders automatically handle both J2K and HTJ2K codestreams
  - id: d30-06-02
    decision: Use 3-bit pass count encoding for HT mode (1-6 range) vs EBCOT variable-length (1-164)
    rationale: ITU-T T.814 specifies smaller pass count range for HT coding; simpler and correct encoding
  - id: d30-06-03
    decision: Detect HT mode by checking if blockCoder is HtBlockEncoder rather than adding explicit flag
    rationale: Avoids API surface expansion; the block coder type is the canonical indicator of HT mode

# Metrics
duration: ~12 minutes
completed: 2026-02-08
tests-before: 2782 (2727 pass, 55 skip)
tests-after: 2806 (2751 pass, 55 skip)
---

# Phase 30 Plan 06: HTJ2K Codec Integration Summary

Integrated the HT block coder into the full HTJ2K codec pipeline with CAP marker support, HT-specific encoder options, and auto-detection on decode.

## What Was Done

### Task 1: HtEncoderOptions, Tier-2 HT pass handling, and CAP marker

Created `HtEncoderOptions` readonly record struct with:
- HtSetCount (1 or 2), IncludeSigProp, IncludeMagRef, TargetBpp, TargetPsnr
- EffectivePassCount computed property (1-6 based on configuration)
- IsLossless computed property
- Five named presets: Lossless (6 passes, no target), Diagnostic (6 passes, 40dB), Archive (6 passes, 35dB), Review (3 passes, 30dB), Fast (1 pass, 25dB)

Updated `Htj2kCodecOptions` to include `HtOptions` property with mode-based defaults (Lossless preset for lossless, Diagnostic for lossy) via `EffectiveHtOptions`.

Added CAP marker (0xFF50) support to `J2kCodestream`:
- `J2kMarkers.CAP` constant
- `HtMode` enum (None, HtOnly, HtDeclared, Mixed)
- Properties: `Pcap`, `Ccap15`, `IsHtj2k`, `HtCodingMode`, `HtPrecision`
- `BuildCapMarker` static method generating correct Pcap (bit 17 for Part 15) and Ccap[15] (HTONLY flag + precision)
- `ParseCapMarker` extracting HT mode from Ccap[15] bit layout

Updated Tier-2 packet encoder/decoder for HT pass count format:
- `PacketEncoder`: `isHtMode` parameter + `WriteNumPassesHt` method (3-bit encoding for 1-6 passes)
- `PacketDecoder`: `IsHtMode` property + HT branch in `ReadNumPasses` (3-bit decoding)

### Task 2: Htj2kCodec HT routing and integration tests

Updated `J2kEncoder`:
- Added `EncodeFrame` overload accepting `IBlockCoder` parameter
- Changed `EncodeComponentCodeBlocks` to accept `IBlockCoder` instead of `EbcotBlockCoder`
- Auto-propagates `isHtMode` to Tier-2 packet encoder when blockCoder is `HtBlockEncoder`

Updated `J2kDecoder`:
- Added `DecodeFrame` overload accepting nullable `IBlockCoder`
- Auto-detects HT mode from CAP marker when blockCoder is null
- Sets `PacketDecoder.IsHtMode` based on `header.IsHtj2k`

Updated `Htj2kCodecBase`:
- Encode: uses `HtBlockEncoder.Instance` instead of EBCOT, generates CAP marker via `BuildCapMarker`
- Decode: passes null blockCoder for auto-detection from CAP marker

Created 24 integration tests covering:
- CAP marker presence and content (8-bit, 16-bit, HTONLY flag, BuildCapMarker correctness)
- HtEncoderOptions preset validation (all 5 presets)
- Htj2kCodecOptions default mapping
- Lossless roundtrips (8-bit, 12-bit, 16-bit) - pixel-perfect
- Lossy encode/decode with quality measurement
- Multi-frame (3 frames) encode/decode
- RPCL progression verification
- Codec registry resolution (all 3 HTJ2K codecs)
- HT block coder usage verification
- Decode auto-detection (HT from CAP marker, EBCOT fallback for standard J2K)
- Validation of HT output

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] J2kEncoder/J2kDecoder did not accept IBlockCoder parameter**
- **Found during:** Task 2
- **Issue:** The plan assumed J2kEncoder/J2kDecoder already accepted IBlockCoder from plan 30-02, but they hardcoded EbcotBlockCoder.Instance
- **Fix:** Added overloads accepting IBlockCoder, kept backward-compatible parameterless versions
- **Files modified:** J2kEncoder.cs, J2kDecoder.cs

**2. [Rule 1 - Bug] Tier-2 pass count format mismatch between encode and decode**
- **Found during:** Task 2 (first test run)
- **Issue:** Encoder wrote EBCOT-style variable-length pass counts but decoder tried to read HT-style 3-bit pass counts when IsHtMode was true
- **Fix:** Auto-propagate isHtMode flag from block coder type to packet encoder during encoding
- **Files modified:** J2kEncoder.cs

## Test Results

- Before: 2782 total (2727 pass, 55 skip, 0 fail)
- After: 2806 total (2751 pass, 55 skip, 0 fail)
- New tests: 24 (all pass)
- Regressions: 0

## Next Phase Readiness

The HTJ2K codec pipeline is now fully integrated with the HT block coder. The encode path uses `HtBlockEncoder.Instance`, generates CAP markers, and produces valid HTJ2K codestreams. The decode path auto-detects HT mode from CAP markers and routes to the appropriate block coder.

Remaining work in Phase 30:
- Plan 07: Rate control and quality layer optimization
- Plan 08: Performance benchmarks
- Plan 09: Conformance testing
- Plan 10: Documentation and cleanup
