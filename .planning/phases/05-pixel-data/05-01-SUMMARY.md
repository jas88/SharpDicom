---
phase: 05-pixel-data
plan: 01
subsystem: pixel-data
tags: [pixel-data, fragments, encapsulated, offset-table, metadata]
dependencies:
  requires: [04-02]
  provides:
    - PixelDataInfo struct with frame size calculation
    - PixelDataHandling enum for loading strategies
    - PixelDataLoadState enum for tracking load state
    - Enhanced DicomFragmentSequence with offset table parsing
    - FragmentParser for encapsulated pixel data
  affects: [05-02, 05-03]
tech-stack:
  added: []
  patterns:
    - Nullable properties for missing metadata
    - Static factory method (FromDataset)
    - Lazy offset table parsing
decisions:
  - title: Two PixelDataInfo types - Data vs Codecs namespaces
    rationale: Different use cases - Data.PixelDataInfo has nullable properties for extraction from datasets where tags may be missing; Codecs.PixelDataInfo has non-nullable for actual encoding/decoding
    alternatives: Single type with nullable or non-nullable
    tradeoffs: Namespace ambiguity requires explicit qualification in some places
  - title: Lazy ParsedBasicOffsets property
    rationale: Parse offset table on first access rather than construction for better performance
    alternatives: Parse immediately in constructor
    tradeoffs: Slightly delayed first access but avoids parsing when not needed
  - title: Extended Offset Table support
    rationale: Required for DICOM files > 4GB with many frames
    alternatives: Only support 32-bit offsets
    tradeoffs: Additional complexity but necessary for large files
key-files:
  created:
    - src/SharpDicom/Data/PixelDataHandling.cs: Enum for loading strategies
    - src/SharpDicom/Data/PixelDataLoadState.cs: Enum for load state tracking
    - src/SharpDicom/Data/PixelDataInfo.cs: Pixel data metadata struct
    - src/SharpDicom/IO/FragmentParser.cs: Encapsulated pixel data parser
    - tests/SharpDicom.Tests/Data/PixelDataInfoTests.cs: 26 tests
    - tests/SharpDicom.Tests/IO/FragmentParserTests.cs: 19 tests
  modified:
    - src/SharpDicom/Data/DicomTag.WellKnown.cs: Added 10 pixel data tags
    - src/SharpDicom/Data/DicomFragmentSequence.cs: Extended with offset table parsing
    - src/SharpDicom/Codecs/IPixelDataCodec.cs: Fixed PixelDataInfo ambiguity
metrics:
  duration: 6 minutes
  tests-added: 45
  tests-total: 616
  completed: 2026-01-27
---

# Phase 5 Plan 1: Core Pixel Data Types Summary

**One-liner:** Created pixel data metadata types, loading strategy enums, and fragment parsing infrastructure for encapsulated pixel data.

## What Was Built

### Core Functionality

1. **PixelDataHandling Enum**
   - `LoadInMemory` - Load pixel data immediately during parse
   - `LazyLoad` - Keep stream reference, load on first access
   - `Skip` - Skip pixel data entirely (metadata-only)
   - `Callback` - Let callback decide per-instance

2. **PixelDataLoadState Enum**
   - `NotLoaded` - Skipped during initial parse
   - `Loading` - Async load in progress
   - `Loaded` - Data available in memory
   - `Failed` - Load failed with error

3. **PixelDataInfo Struct**
   - Properties: Rows, Columns, BitsAllocated, BitsStored, HighBit, SamplesPerPixel, NumberOfFrames, PlanarConfiguration, PixelRepresentation, PhotometricInterpretation
   - Computed: BytesPerSample, FrameSize, TotalSize, HasImageDimensions
   - Static method: `FromDataset(DicomDataset)` extracts values from dataset
   - All properties nullable to handle missing tags gracefully

4. **Enhanced DicomFragmentSequence**
   - `ParsedBasicOffsets` - Lazy-parsed 32-bit offsets from Basic Offset Table
   - `ParsedExtendedOffsets` - Lazy-parsed 64-bit offsets (for >4GB files)
   - `ParsedExtendedLengths` - Lazy-parsed 64-bit frame lengths
   - `ExtendedOffsetTable` / `ExtendedOffsetTableLengths` properties
   - `FragmentCount` and `TotalSize` computed properties
   - Static methods: `ParseBasicOffsetTable`, `ParseExtendedOffsetTable`

5. **FragmentParser**
   - `ParseEncapsulated(data, tag, vr, littleEndian)` - Parses encapsulated pixel data
   - Handles Basic Offset Table (first item, may be empty)
   - Extracts fragments from Item tags
   - Stops at Sequence Delimitation tag
   - Supports both little-endian and big-endian
   - Detailed error messages for malformed data

### Tags Added to DicomTag.WellKnown

- Rows (0028,0010)
- Columns (0028,0011)
- BitsStored (0028,0101)
- HighBit (0028,0102)
- SamplesPerPixel (0028,0002)
- NumberOfFrames (0028,0008)
- PlanarConfiguration (0028,0006)
- PhotometricInterpretation (0028,0004)
- ExtendedOffsetTable (7FE0,0001)
- ExtendedOffsetTableLengths (7FE0,0002)

## Test Coverage

### PixelDataInfoTests (26 tests)
- Frame size calculation for various dimensions (8-bit, 16-bit, RGB)
- Null handling for missing dimensions
- TotalSize calculation for multi-frame
- BytesPerSample calculation (8, 12, 16, 24, 32 bits)
- FromDataset extraction
- NumberOfFrames parsing with whitespace

### FragmentParserTests (19 tests)
- Single fragment with empty BOT
- Multiple fragments with BOT
- Empty BOT validation
- Basic Offset Table parsing (4 offsets)
- Extended Offset Table parsing (64-bit)
- Error cases: missing BOT, unexpected tags, truncated data

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed PixelDataInfo ambiguity in IPixelDataCodec**
- **Found during:** Task 3 (unit tests)
- **Issue:** `SharpDicom.Data.PixelDataInfo` conflicts with `SharpDicom.Codecs.PixelDataInfo`
- **Fix:** Changed IPixelDataCodec to use explicit `Codecs.PixelDataInfo` qualification
- **Files modified:** src/SharpDicom/Codecs/IPixelDataCodec.cs
- **Commit:** 0cc890d

## Next Phase Readiness

Phase 5 Plan 2 can proceed with:
- DicomPixelData wrapper class for accessing pixel data
- Integration with PixelDataHandling in reader options
- Lazy loading implementation with stream references

All required types are in place for pixel data handling infrastructure.

## Technical Notes

1. **Two PixelDataInfo Types**: Design intentionally has two types:
   - `SharpDicom.Data.PixelDataInfo` - Nullable properties for dataset extraction
   - `SharpDicom.Codecs.PixelDataInfo` - Non-nullable for codec operations

2. **Frame Size Calculation**: `Rows * Columns * SamplesPerPixel * BytesPerSample`
   - Returns null if any dimension missing
   - BytesPerSample = (BitsAllocated + 7) / 8

3. **Extended Offset Table**: Supports DICOM files > 4GB with 64-bit offsets
