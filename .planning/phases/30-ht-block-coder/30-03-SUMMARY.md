---
phase: 30-ht-block-coder
plan: 03
subsystem: codecs/jpeg2000/tier1
tags: [vlc, mel, ht-block-coder, bitio, jpeg2000, htj2k]
dependency-graph:
  requires: []
  provides:
    - VlcTable static class with Table0 and Table1 (1024 entries each)
    - MelDecoder ref struct with 13-state run-length decoding
    - MelEncoder ref struct with 13-state run-length encoding
    - HtCleanupReader ref struct for three-stream segment reading
    - HtCleanupWriter ref struct for three-stream segment writing
  affects:
    - 30-04+ (HT cleanup pass uses VlcTable, MelDecoder, HtCleanupReader)
    - 30-05+ (HT SigProp/MagRef passes may share HtBitIO patterns)
tech-stack:
  added: []
  patterns:
    - Lazy<T> for thread-safe table initialization
    - Bit-reversed codeword indexing for VLC lookup tables
    - Partial run encoding (MelE[state] bits after break signal)
    - Three-stream bidirectional segment layout with ILW
key-files:
  created:
    - src/SharpDicom/Codecs/Jpeg2000/Tier1/VlcTable.cs
    - src/SharpDicom/Codecs/Jpeg2000/Tier1/MelCoder.cs
    - src/SharpDicom/Codecs/Jpeg2000/Tier1/HtBitIO.cs
    - tests/SharpDicom.Tests/Codecs/Jpeg2000/Tier1/VlcTableTests.cs
    - tests/SharpDicom.Tests/Codecs/Jpeg2000/Tier1/MelCoderTests.cs
    - tests/SharpDicom.Tests/Codecs/Jpeg2000/Tier1/HtBitIOTests.cs
  modified: []
decisions:
  - id: vlc-bit-reversal
    description: VLC codewords stored MSB-first in source, bit-reversed to LSB-first for table indexing
    rationale: VLC stream bits are consumed LSB-first; table index must match raw stream bit order
  - id: mel-partial-run
    description: MEL partial runs encoded as MelE[state] bits after break signal
    rationale: Without partial run encoding, decoder cannot determine how many insignificant quads preceded a significant one within a broken run
  - id: mel-no-byte-stuffing
    description: MEL stream does not use JPEG byte stuffing (0xFF handling)
    rationale: Byte stuffing is specific to MQ coder output; MEL stream uses simple 8-bit bytes
metrics:
  duration: 23m
  completed: 2026-02-07
---

# Phase 30 Plan 03: HT Primitive Components Summary

VLC lookup tables, MEL run-length coder, and three-stream bidirectional bit I/O for HT block coding.

## One-Liner

Two VLC decode tables (1024 entries, bit-reversed LSB-first indexing), 13-state MEL encoder/decoder with partial run encoding, and HtCleanupReader/Writer managing ILW-delimited three-stream segments.

## What Was Done

### Task 1: VLC Lookup Tables and MEL Coder

Created `VlcTable` as a static class with two Lazy-initialized lookup tables (Table0, Table1) derived from ITU-T T.814. Each table has 1024 entries indexed by 3-bit context + 7-bit codeword. Entries are packed ushorts encoding significance pattern (4 bits), embedded magnitude bits (4 bits), and codeword length (4 bits).

Key implementation detail: VLC codewords are defined MSB-first in the specification but the stream is consumed LSB-first. The `FillVlcEntries` method bit-reverses each codeword before writing to the table, ensuring correct lookup when indexing by raw stream bits.

Created `MelCoder` (static constants), `MelDecoder` (ref struct), and `MelEncoder` (ref struct):
- 13-state adaptive run-length coding with MelE = {0,0,0,1,1,1,2,2,2,3,3,4,5}
- Full run: 0-bit signals 2^MelE[state] insignificant quads, state transitions up
- Broken run: 1-bit followed by MelE[state] bits encoding partial run count, state transitions down
- Decoder tracks `_significantPending` flag to properly sequence insignificant quads before the significant one in partial runs

### Task 2: HtBitIO Three-Stream Reader/Writer and Tests

Created `HtCleanupReader` (ref struct) that parses a cleanup codeword segment:
- Extracts 12-bit ILW (Interface Locator Word) from last 2 bytes
- MagSgn stream: forward reader from byte 0 to ILW offset
- VLC stream: forward reader from ILW offset
- MEL stream: backward reader via embedded MelDecoder
- Methods: `ReadMagSgnBits`, `PeekVlcBits`, `AdvanceVlc`, `ReadVlcBits`, `DecodeMelSignificance`

Created `HtCleanupWriter` (ref struct) with separate ArrayPool-backed buffers for each stream:
- `WriteMagSgnBits`, `WriteVlcBits`, `EncodeMel` write to independent streams
- `Finalize()` merges streams with ILW at end: [MagSgn][VLC][MEL reversed][ILW 2B]

Created comprehensive test suites:
- **VlcTableTests** (50 tests): table structure, all 8 contexts populated, completeness, decode method verification, thread safety
- **MelCoderTests** (32 tests): state table values, state transitions, run lengths, encoder/decoder roundtrips with alternating/all-insignificant/all-significant/long-run patterns
- **HtBitIOTests** (21 tests): ILW parsing, MagSgn/VLC/MEL stream reading, writer operations, roundtrips for individual and combined streams

## Decisions Made

| Decision | Rationale | Confidence |
|----------|-----------|------------|
| Bit-reverse codewords for table index | Stream bits are LSB-first; index must match raw bit order | High |
| Partial run encoding in MEL | Without it, decoder loses track of insignificant quads in broken runs | High |
| No byte stuffing in MEL stream | Only MQ coder uses JPEG byte stuffing; MEL is simpler | High |
| Lazy<T> for VLC table init | Thread-safe, computed once on first access, no contention | High |

## Deviations from Plan

None - plan executed exactly as written.

## Test Results

- 103 new tests (50 VLC + 32 MEL + 21 HtBitIO)
- Full suite: 2663 total (2608 pass, 55 skipped, 0 failed)
- Zero regressions

## Commits

| Hash | Message |
|------|---------|
| 58e318f | feat(30-03): VLC lookup tables and MEL run-length coder |
| 55aeaf9 | feat(30-03): HtBitIO three-stream reader/writer and unit tests |

## Next Phase Readiness

These primitives are ready for use by the HT cleanup pass (Plan 04+):
- VlcTable.DecodeTable0/DecodeTable1 for quad significance decoding
- MelDecoder for insignificant quad run detection
- HtCleanupReader for parsing cleanup codeword segments
- HtCleanupWriter for constructing cleanup codeword segments
