---
phase: 07-file-writing
plan: 01
subsystem: io
tags: [dicom, writing, buffer-writer, transfer-syntax]
dependency-graph:
  requires: [01-05, 01-01, 01-03]
  provides: [DicomStreamWriter, DicomWriterOptions, SequenceLengthEncoding]
  affects: [07-02, 07-03]
tech-stack:
  added: []
  patterns: [IBufferWriter, GetSpan/Advance, zero-copy]
key-files:
  created:
    - src/SharpDicom/IO/DicomWriterOptions.cs
    - src/SharpDicom/IO/DicomStreamWriter.cs
    - tests/SharpDicom.Tests/IO/DicomStreamWriterTests.cs
  modified:
    - src/SharpDicom/Data/TransferSyntax.cs
    - src/SharpDicom.Generators/SharpDicom.Generators.csproj
    - src/SharpDicom.Generators/Parsing/PrivateDictParser.cs
    - src/SharpDicom.Generators/Emitters/VendorDictionaryEmitter.cs
decisions:
  - id: writer-pattern
    choice: IBufferWriter<byte> GetSpan/Advance pattern
    rationale: Zero-copy writing to any buffer target (Stream, PipeWriter, ArrayBufferWriter)
  - id: dual-constructor
    choice: Support both options-based and explicit parameter construction
    rationale: Flexibility for different use cases (file writing vs protocol testing)
metrics:
  duration: 7m
  completed: 2026-01-27
---

# Phase 7 Plan 01: DicomStreamWriter Summary

Low-level IBufferWriter-based DICOM element writing for efficient file serialization.

## What Was Done

### Task 1: DicomWriterOptions and SequenceLengthEncoding
- Created `DicomWriterOptions` class with:
  - `TransferSyntax` property (default: ExplicitVRLittleEndian)
  - `SequenceLength` property (Undefined/Defined modes)
  - `BufferSize` property (default: 80KB)
- Added `SequenceLengthEncoding` enum (Undefined uses delimiters, Defined uses calculated lengths)
- Added `DeflatedExplicitVRLittleEndian` transfer syntax (1.2.840.10008.1.2.1.99)
- Updated `TransferSyntax.FromUID` to recognize deflated TS

### Task 2: DicomStreamWriter Implementation
- Created `DicomStreamWriter` class implementing IBufferWriter<byte> writing pattern
- Support for Explicit VR encoding with correct header sizes:
  - 8-byte header for 16-bit length VRs (AE, AS, AT, CS, DA, DS, DT, FL, FD, IS, LO, LT, PN, SH, SL, SS, ST, TM, UI, UL, US)
  - 12-byte header for 32-bit length VRs (OB, OD, OF, OL, OV, OW, SQ, SV, UC, UN, UR, UT, UV)
- Support for Implicit VR encoding with 8-byte headers (no VR field)
- Support for Big Endian byte order
- Automatic value padding using DicomVRInfo.PaddingByte:
  - Space (0x20) for string VRs
  - Null (0x00) for binary VRs and UI

### Task 3: Comprehensive Tests
- Added 39 tests covering:
  - All 16-bit length VRs (AE, CS, DA, DS, IS, LO, PN, SH, UI, US, UL, FL, FD, TM)
  - All 32-bit length VRs (OB, OW, UN, UC, UT)
  - Implicit VR encoding
  - Big endian byte order
  - Value padding (odd/even lengths, space vs null padding)
  - Edge cases (empty values, tag-only writes, raw bytes)
- Total test file: 959 lines

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Generator nullable annotations**
- **Found during:** Task 2 build
- **Issue:** SharpDicom.Generators project had `<Nullable>disable</Nullable>` but files used `string?` annotations
- **Fix:** Changed to `<Nullable>enable</Nullable>` and added null-forgiving operators where null checks already existed
- **Files modified:** src/SharpDicom.Generators/SharpDicom.Generators.csproj, src/SharpDicom.Generators/Parsing/PrivateDictParser.cs
- **Commit:** b499fc5

**2. [Rule 3 - Blocking] VendorDictionaryEmitter byte literal**
- **Found during:** Task 3 test run
- **Issue:** Hex literals `0x` in tuple array were int type, not byte, causing type mismatch
- **Fix:** Added `(byte)` cast to hex literals in generated code
- **Files modified:** src/SharpDicom.Generators/Emitters/VendorDictionaryEmitter.cs
- **Commit:** 6feab85

## Commits

| Hash | Type | Description |
|------|------|-------------|
| 4f986fc | feat | Add DicomWriterOptions and DeflatedExplicitVRLittleEndian |
| b499fc5 | feat | Implement DicomStreamWriter for IBufferWriter<byte> |
| 6feab85 | test | Add comprehensive DicomStreamWriter tests |

## Key Implementation Details

### Element Writing Algorithm
```csharp
// Explicit VR 16-bit: Tag(4) + VR(2) + Length(2) + Value
// Explicit VR 32-bit: Tag(4) + VR(2) + Reserved(2) + Length(4) + Value
// Implicit VR: Tag(4) + Length(4) + Value

// Padding: (length & 1) == 1 triggers padding
// Padding byte from DicomVRInfo.PaddingByte
```

### Usage Pattern
```csharp
var buffer = new ArrayBufferWriter<byte>();
var writer = new DicomStreamWriter(buffer, options);

writer.WriteElement(element);           // From IDicomElement
writer.WriteElement(tag, vr, value);    // Explicit parameters
writer.WriteTag(tag);                   // Tag only (delimiters)
writer.WriteTagWithLength(tag, length); // Tag + length (item markers)
```

## Test Coverage

- 39 new tests for DicomStreamWriter
- All existing tests pass (655 -> 667 total)
- Tests verify byte-level encoding correctness

## Next Phase Readiness

Plan 07-01 provides the foundation for:
- **07-02 (Part10Writer):** Will use DicomStreamWriter for dataset encoding
- **07-03 (DicomFileWriter):** Will compose Part10Writer with async stream support

No blockers identified for subsequent plans.
