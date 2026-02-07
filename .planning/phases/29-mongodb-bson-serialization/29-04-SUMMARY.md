---
phase: 29-mongodb-bson-serialization
plan: 04
subsystem: serialization-tests
tags: [bson, dicom-json, nunit, roundtrip, test-suite, serialization]

# Dependency graph
requires:
  - phase: 29-01
    provides: BsonDocumentBuffer, BsonDicomWriter, BsonSerializationOptions
  - phase: 29-02
    provides: BsonDicomReader, DicomDatasetBsonExtensions
  - phase: 29-03
    provides: DicomJsonWriter, DicomJsonReader
provides:
  - Comprehensive BSON serialization test suite (51 tests)
  - Comprehensive DICOM-JSON serialization test suite (22 tests)
  - Roundtrip fidelity verification for all VR types
  - Dual-storage exact string preservation verification
affects: [29-05-integration]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Parametric roundtrip testing pattern (serialize, deserialize, compare element-by-element)"
    - "JSON structure validation via JsonDocument for DICOM-JSON compliance"
    - "AlwaysIncludeVR option used for tests with non-standard VR/tag pairings"

key-files:
  created:
    - tests/SharpDicom.Tests/Serialization/BsonDocumentBufferTests.cs
    - tests/SharpDicom.Tests/Serialization/BsonDicomWriterTests.cs
    - tests/SharpDicom.Tests/Serialization/BsonDicomReaderTests.cs
    - tests/SharpDicom.Tests/Serialization/BsonRoundtripTests.cs
    - tests/SharpDicom.Tests/Serialization/DicomJsonWriterTests.cs
    - tests/SharpDicom.Tests/Serialization/DicomJsonReaderTests.cs
    - tests/SharpDicom.Tests/Serialization/DicomJsonRoundtripTests.cs
  modified: []

decisions:
  - id: d29-04-01
    title: "Use AlwaysIncludeVR for tests with non-standard VR/tag pairings"
    context: "Some test tags have dictionary VRs different from test VRs (e.g., SpacingBetweenSlices is DS in dictionary but tested as FD). Without explicit VR, deserialization uses dictionary VR."
    decision: "Use AlwaysIncludeVR = true for tests that exercise non-standard VR/tag combinations"
    outcome: "Roundtrip tests pass for all VR types while accurately testing the VR preservation path"
  - id: d29-04-02
    title: "ASCII-only component groups for PN tests"
    context: "DicomStringElement.GetString() uses DicomEncoding.Default (ASCII). Japanese characters in PN component groups get garbled without SpecificCharacterSet."
    decision: "Use ASCII-safe PN values in serialization tests; non-ASCII encoding is a character set concern, not serialization"
    outcome: "PN tests verify component group roundtrip without coupling to encoding subsystem"
  - id: d29-04-03
    title: "Skip IS/DS exact string comparison in DICOM-JSON roundtrip"
    context: "DICOM-JSON serializes IS/DS as JSON numbers per PS3.18. Deserialization reconstructs strings from numbers, losing original formatting (e.g., '042' becomes '42')."
    decision: "DICOM-JSON roundtrip tests skip IS/DS string comparison; BSON roundtrip tests verify exact string preservation via Raw field"
    outcome: "Test expectations match actual standard behavior for each format"

metrics:
  duration: "~6 minutes"
  completed: 2026-02-07
---

# Phase 29 Plan 04: Serialization Test Suite Summary

73 new tests covering BSON and DICOM-JSON serialization with zero failures and zero regressions.

## What Was Built

### BsonDocumentBufferTests (10 tests)
Low-level buffer correctness tests verifying:
- Int32/Int64 little-endian byte ordering
- Double IEEE 754 bit-level fidelity via BitConverter roundtrip
- CString null termination (UTF-8 bytes + 0x00)
- BsonString length-prefixed null-terminated format (int32 length + bytes + 0x00)
- Deferred document size patching (BeginDocument/EndDocument)
- Nested document size correctness
- Buffer growth via EnsureCapacity
- Reset returns Position to 0
- ToArray copies only written bytes

### BsonDicomWriterTests (16 tests)
Serialization output verification:
- Empty dataset produces minimal 5-byte BSON document
- String elements produce {"Value": ["str"]} structure
- IS dual-storage: Value array has parsed integers, Raw has original string
- DS dual-storage: Value array has parsed doubles, Raw has original string
- DA/TM dual-storage roundtrip preserves original date/time strings
- PN component group parsing (Alphabetic/Ideographic/Phonetic)
- Numeric VRs map to correct BSON types (SS->Int32, US->Int32, FD->Double)
- Sequence produces array of nested documents
- Binary element inlines below threshold
- Binary element delegates to ExternalBinaryHandler above threshold
- Private tags grouped under _private sub-document by creator
- StripPrivateTags=true omits all private elements
- Tag key format: Hex8 (default) and Keyword formats
- VR field inclusion: only for ambiguous/retired/private tags by default

### BsonDicomReaderTests (10 tests)
Deserialization correctness:
- Minimal 5-byte document returns empty dataset
- String element restoration
- IS dual-storage uses Raw field (preserves "042" vs "42")
- PN restores original string from Value array
- Numeric element binary reconstruction
- Sequence with multiple nested items
- Binary element byte-level restoration
- Private tags restored with creator name registration
- All tag key formats parsed (Hex8, Dotted, Keyword)
- Missing VR falls back to dictionary lookup

### BsonRoundtripTests (15 tests)
End-to-end fidelity:
- All 17 string VRs (AE, AS, CS, DA, DS, DT, IS, LO, LT, PN, SH, ST, TM, UC, UI, UR, UT)
- All numeric VRs (SS, US, SL, UL, FL, FD, AT) with AlwaysIncludeVR
- Binary VRs (OB, OW)
- Sequence with nested elements
- Sequence depth limit fallback
- Private tags preserve creator names
- Empty elements (string and numeric)
- Multi-valued elements (backslash-delimited)
- IS preserves exact string "042" (not "42")
- DS preserves exact string "3.14000" (not "3.14")
- DA preserves "20240115"
- TM preserves "120000.000000"
- PN preserves Alphabetic=Ideographic=Phonetic groups
- Large dataset with 20+ elements of all types
- IBufferWriter overload produces identical bytes to byte[] overload

### DicomJsonWriterTests (8 tests)
JSON output structure:
- Empty dataset produces "{}"
- vr field always present (PS3.18 requirement)
- PN uses {"Alphabetic":"..."} object format
- IS produces JSON number values
- SQ produces nested JSON objects
- InlineBinary is base64-encoded
- BulkDataURI for large binary with external handler
- Empty elements omit Value field entirely

### DicomJsonReaderTests (6 tests)
JSON deserialization:
- Empty JSON object returns empty dataset
- String element from Value array
- PN reconstruction from Alphabetic object
- InlineBinary base64 decoding
- Sequence with nested datasets
- Unknown VR handling

### DicomJsonRoundtripTests (8 tests)
End-to-end JSON fidelity with structure validation:
- All string VRs roundtrip
- All numeric VRs roundtrip
- Binary VRs roundtrip via InlineBinary
- Sequence roundtrip
- PN roundtrip
- Multi-valued elements roundtrip
- Empty elements roundtrip
- Large dataset with all types

## Deviations from Plan

None -- plan executed exactly as written.

## Decisions Made

1. **AlwaysIncludeVR for non-standard VR/tag pairings**: Tests that assign VRs different from dictionary entries (e.g., FD on a DS tag) use AlwaysIncludeVR=true to ensure the explicit VR survives roundtrip.

2. **ASCII-only PN component groups**: Tests use ASCII-safe PN values because DicomStringElement.GetString() defaults to ASCII encoding. Non-ASCII requires SpecificCharacterSet, which is an encoding concern, not serialization.

3. **IS/DS exact string skip in DICOM-JSON**: DICOM-JSON converts IS/DS to JSON numbers per PS3.18, losing original formatting. BSON tests verify exact string preservation via the Raw field.

## Test Results

| Suite | Tests | Status |
|-------|-------|--------|
| BsonDocumentBufferTests | 10 | All pass |
| BsonDicomWriterTests | 16 | All pass |
| BsonDicomReaderTests | 10 | All pass |
| BsonRoundtripTests | 15 | All pass |
| DicomJsonWriterTests | 8 | All pass |
| DicomJsonReaderTests | 6 | All pass |
| DicomJsonRoundtripTests | 8 | All pass |
| **Total** | **73** | **All pass** |

Total test count: 2511 (up from 2438). Zero regressions.
