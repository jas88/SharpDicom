---
phase: 30-ht-block-coder
plan: 01
subsystem: codecs/jpeg2000
tags: [dwt, subband, ebcot, code-block, jpeg2000]
dependency-graph:
  requires: []
  provides:
    - SubbandType enum (LL=0, HL=1, LH=2, HH=3)
    - SubbandDescriptor readonly struct
    - SubbandPartitioner static class
    - TileComponent class with pooled coefficient storage
  affects:
    - 30-02 (EBCOT context table fix needs SubbandType)
    - 30-04+ (HT block coder uses TileComponent for coefficient access)
tech-stack:
  added: []
  patterns:
    - ArrayPool for large coefficient buffers
    - Readonly struct for fixed descriptor data
    - Multi-TFM conditional compilation for ThrowIfNull
key-files:
  created:
    - src/SharpDicom/Codecs/Jpeg2000/Subband/SubbandDescriptor.cs
    - src/SharpDicom/Codecs/Jpeg2000/Subband/SubbandPartitioner.cs
    - src/SharpDicom/Codecs/Jpeg2000/Subband/TileComponent.cs
    - tests/SharpDicom.Tests/Codecs/Jpeg2000/Subband/SubbandPartitionerTests.cs
    - tests/SharpDicom.Tests/Codecs/Jpeg2000/Subband/TileComponentTests.cs
  modified: []
decisions:
  - id: subband-ordering
    title: SubbandType enum values match existing EBCOT convention
    rationale: Existing DwtTransform.GetSubbandDimensions and EbcotEncoder use 0=LL, 1=HL, 2=LH, 3=HH; plan originally specified different ordering
metrics:
  duration: ~10min
  completed: 2026-02-08
---

# Phase 30 Plan 01: Subband Infrastructure Summary

Multi-resolution subband descriptors and tile-component coefficient management for DWT-decomposed JPEG 2000 code-blocks.

## What Was Built

Three new types in `Codecs/Jpeg2000/Subband/`:

### SubbandDescriptor (readonly struct)
- `SubbandType` enum: LL=0, HL=1, LH=2, HH=3
- Resolution level, dimensions, origin coordinates, code-block grid size
- IEquatable implementation with netstandard2.0-compatible GetHashCode

### SubbandPartitioner (static class)
- `GetSubbands()`: Computes all subband descriptors for a given image size and decomposition
- Uses ITU-T T.800 Section B.5 formulas: LL=ceil(W/2)xceil(H/2), HL=floor(W/2)xceil(H/2), LH=ceil(W/2)xfloor(H/2), HH=floor(W/2)xfloor(H/2)
- Recursive decomposition of LL at each level
- `GetSubbandForCodeBlock()`: Returns SubbandType for a code-block at a given position
- `FindSubbandAt()`: Locates subband containing a given coefficient position

### TileComponent (sealed class, IDisposable)
- Manages coefficient data for a single tile + component
- Uses ArrayPool for buffers >= 1024 elements
- `GetCodeBlockCoefficients()`: Extracts code-block region with edge handling
- `SetCodeBlockCoefficients()`: Writes back code-block data for reconstruction
- Handles partial code-blocks at subband boundaries

## Test Coverage

49 tests covering:
- Subband count formula (1 + 3*N levels)
- Dimension correctness for 256x256, 255x255 (odd), 1x1, 512x256 (non-square)
- Off-by-one verification: ceil/floor width/height sums equal parent dimension
- Code-block grid computation with partial blocks
- GetSubbandForCodeBlock type mapping
- TileComponent get/set roundtrip
- Edge code-blocks (partial width/height)
- Disposal and argument validation

## Decisions Made

| ID | Decision | Rationale |
|----|----------|-----------|
| subband-ordering | SubbandType uses LL=0, HL=1, LH=2, HH=3 | Matches existing convention in DwtTransform.GetSubbandDimensions and EbcotEncoder.GetSignificanceContextFromCounts; plan originally specified LL=0, LH=1, HL=2, HH=3 which would have broken integration |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] SubbandType ordering corrected to match existing codebase**
- **Found during:** Task 1 design
- **Issue:** Plan specified LL=0, LH=1, HL=2, HH=3 but existing DwtTransform and EbcotEncoder consistently use LL=0, HL=1, LH=2, HH=3
- **Fix:** Used HL=1, LH=2 ordering to match existing code
- **Files:** SubbandDescriptor.cs

**2. [Rule 3 - Blocking] ArgumentNullException.ThrowIfNull not available on netstandard2.0**
- **Found during:** Task 1 build verification
- **Issue:** CA1510 analyzer requires ThrowIfNull but it doesn't exist on netstandard2.0
- **Fix:** Used `#if NET6_0_OR_GREATER` conditional compilation, matching pattern in FileSystemDicomStore.cs
- **Files:** SubbandPartitioner.cs

## Commits

| Hash | Description |
|------|-------------|
| 8ac33fd | feat(30-01): add SubbandDescriptor and SubbandPartitioner types |
| 70e2170 | feat(30-01): add TileComponent and subband unit tests |

## Next Phase Readiness

The subband infrastructure is ready for:
1. **Plan 30-02**: EBCOT context table fix - can now use `SubbandType` enum instead of hardcoded `int subbandType = 0`
2. **Plan 30-04+**: HT block coder can use `TileComponent` for coefficient access with proper subband awareness

No blockers identified.
