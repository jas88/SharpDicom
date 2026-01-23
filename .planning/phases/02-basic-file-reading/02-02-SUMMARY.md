---
phase: 02-basic-file-reading
plan: 02
subsystem: io
tags: [parsing, part10, file-meta-info, transfer-syntax]

dependency-graph:
  requires: [02-01-stream-reader]
  provides: [part10-parsing, fmi-extraction, transfer-syntax-detection]
  affects: [02-03-file-reader, 03-implicit-vr]

tech-stack:
  added: []
  patterns: [file-structure-parsing, lenient-handling]

key-files:
  created:
    - src/SharpDicom/IO/Part10Reader.cs
    - src/SharpDicom/Data/DicomTag.WellKnown.cs
    - src/SharpDicom/Data/Exceptions/DicomFileException.cs
    - tests/SharpDicom.Tests/IO/Part10ReaderTests.cs
  modified:
    - src/SharpDicom/Data/DicomTag.cs
    - src/SharpDicom/Data/DicomVR.cs

decisions:
  - id: "02-02-001"
    title: "Partial struct for DicomTag"
    choice: "Make DicomTag a partial struct"
    rationale: "Allows well-known constants in separate file without bloating main struct"
  - id: "02-02-002"
    title: "DicomFileException hierarchy"
    choice: "Three-level exception hierarchy: DicomFileException, DicomPreambleException, DicomMetaInfoException"
    rationale: "Enables fine-grained error handling at different parsing stages"

metrics:
  duration: "10 minutes"
  completed: "2026-01-27"
---

# Phase 2 Plan 02: Part10Reader Summary

Part 10 file structure parser for DICOM preamble, DICM prefix, and File Meta Information extraction.

## What Was Built

### Part10Reader

A class that parses the DICOM Part 10 file header structure:

**Key capabilities:**
- Detect and parse 128-byte preamble
- Recognize "DICM" prefix at expected positions (offset 128 or offset 0)
- Parse File Meta Information (Group 0002) elements
- Extract Transfer Syntax from (0002,0010)
- Determine dataset start position for downstream readers
- Handle missing/malformed headers based on DicomReaderOptions

**API:**
```csharp
public sealed class Part10Reader
{
    public Part10Reader(DicomReaderOptions? options = null);

    public bool TryParseHeader(ReadOnlySpan<byte> buffer);

    // Results
    public ReadOnlyMemory<byte> Preamble { get; }      // 128 bytes or empty
    public bool HasDicmPrefix { get; }                  // DICM found
    public DicomDataset? FileMetaInfo { get; }         // Group 0002
    public TransferSyntax TransferSyntax { get; }      // From (0002,0010)
    public int DatasetStartPosition { get; }           // Byte offset for dataset
}
```

### DicomFileException Hierarchy

Exception types for file-level errors:

| Exception | Purpose |
|-----------|---------|
| DicomFileException | Base class for file format errors |
| DicomPreambleException | Missing/invalid preamble or DICM prefix |
| DicomMetaInfoException | Invalid/missing File Meta Information |

### Well-Known DicomTag Constants

Static tag constants for common tags used in file processing:

| Tag | Constant | Description |
|-----|----------|-------------|
| (0002,0000) | FileMetaInformationGroupLength | FMI group length |
| (0002,0001) | FileMetaInformationVersion | FMI version |
| (0002,0002) | MediaStorageSOPClassUID | Media storage SOP class |
| (0002,0003) | MediaStorageSOPInstanceUID | Media storage SOP instance |
| (0002,0010) | TransferSyntaxUID | Transfer syntax |
| (0002,0012) | ImplementationClassUID | Implementation class |
| (0002,0013) | ImplementationVersionName | Implementation version |
| (0002,0016) | SourceApplicationEntityTitle | Source AE title |
| (0008,0005) | SpecificCharacterSet | Character encoding |
| (7FE0,0010) | PixelData | Pixel data |

### VR Helper Properties

Added to DicomVR:

| Property | True For |
|----------|----------|
| IsStringVR | AE, AS, CS, DA, DS, DT, IS, LO, LT, PN, SH, ST, TM, UC, UI, UR, UT |
| IsNumericVR | FL, FD, SL, SS, UL, US, AT |

## Deviations from Plan

None - plan executed exactly as written.

## Test Coverage

23 new tests covering:
- Valid preamble + DICM prefix detection
- DICM at position 0 (no preamble)
- Missing DICM in Strict/Lenient/Permissive modes
- Empty file handling
- Non-DICOM file detection
- Transfer Syntax extraction (Explicit VR LE, Implicit VR LE, JPEG Baseline, unknown)
- File Meta Information element extraction
- Dataset start position calculation
- IsStringVR property verification
- IsNumericVR property verification

## Technical Notes

### Part 10 File Structure

```
┌─────────────────────────────────┐
│ 128-byte preamble (optional)    │ ← Typically zeros
├─────────────────────────────────┤
│ "DICM" prefix (4 bytes)         │ ← Magic bytes
├─────────────────────────────────┤
│ File Meta Information           │ ← Always Explicit VR Little Endian
│ (Group 0002)                    │
├─────────────────────────────────┤
│ Dataset                         │ ← Transfer Syntax from (0002,0010)
└─────────────────────────────────┘
```

### FMI Parsing Strategy

1. Look for DICM prefix at offset 128, then offset 0
2. If found, parse Group 0002 elements using DicomStreamReader
3. Use group length (0002,0000) if present to know FMI boundaries
4. Otherwise, read until group changes from 0002
5. Extract Transfer Syntax from (0002,0010)
6. Default to Implicit VR Little Endian if missing

### Handling Options

| Mode | Preamble | FMI | Behavior |
|------|----------|-----|----------|
| Strict | Required | Required | Throws if missing |
| Lenient | Optional | Optional | Auto-detect, use defaults |
| Permissive | Ignore | Ignore | Assume raw dataset |

## Commits

| Hash | Message |
|------|---------|
| 1deda3f | feat(02-02): implement Part10Reader for DICOM file structure |

## Next Phase Readiness

Ready for:
- **02-03**: DicomFileReader (high-level async file reading using Part10Reader + DicomStreamReader)
- **02-04**: Integration testing with real DICOM files

Dependencies provided for future phases:
- Transfer syntax detection enables implicit VR support (Phase 3)
- FMI parsing provides metadata for higher-level APIs
