---
phase: 29-mongodb-bson-serialization
plan: 02
subsystem: serialization
tags: [bson, mongodb, deserialization, dicom-dataset, round-trip]

# Dependency graph
requires:
  - phase: 29-01
    provides: BsonDicomWriter, BsonType, BsonSerializationOptions, BsonDocumentBuffer
  - phase: 01-core-types
    provides: DicomTag, DicomVR, DicomDataset, element types
provides:
  - BsonDicomReader.Deserialize for BSON-to-DicomDataset conversion
  - DicomDatasetBsonExtensions ToBson/FromBson convenience API
  - Full round-trip capability (serialize then deserialize without data loss)
affects: [29-03-dicom-json, 29-04-round-trip-tests]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Sequential offset-based BSON parsing with typed value dispatch"
    - "Dual-storage reconstruction: Raw field preferred for IS/DS/DA/TM/DT round-trip fidelity"
    - "Private tag re-registration from _private sub-document during deserialization"
    - "Extension method pattern for discoverability (ToBson/FromBson on DicomDataset)"

key-files:
  created:
    - src/SharpDicom/Serialization/Bson/BsonDicomReader.cs
    - src/SharpDicom/Serialization/Bson/DicomDatasetBsonExtensions.cs
  modified: []

key-decisions:
  - "Tag key parsing accepts Hex8, Dotted, and Keyword formats for maximum compatibility"
  - "Sequence items stored as raw byte[] for recursive DeserializeCore calls"
  - "FromBson placed as static method on extensions class (not extension) for discoverability"
  - "Flattened keys detected and skipped during deserialization (informational only)"

patterns-established:
  - "Static BsonDicomReader.Deserialize as entry point for BSON deserialization"
  - "DicomDatasetBsonExtensions for fluent ToBson()/FromBson() API"

# Metrics
duration: 5min
completed: 2026-02-07
---

# Phase 29 Plan 02: BSON Deserialization Summary

**Sequential BSON parser restoring DicomDataset from raw bytes with dual-storage VR reconstruction, private tag re-registration, and ToBson/FromBson convenience API**

## Performance

- **Duration:** 5 min
- **Started:** 2026-02-07T19:25:28Z
- **Completed:** 2026-02-07T19:31:01Z
- **Tasks:** 2
- **Files created:** 2

## Accomplishments

- BsonDicomReader.Deserialize converts raw BSON bytes back to fully functional DicomDataset
- All VR types handled: string, numeric (SS/US/SL/UL/FL/FD/SV/UV/AT), binary (OB/OW/OD/OF/OL/OV/UN), sequence (SQ)
- Dual-storage VRs (IS/DS/DA/TM/DT) reconstruct from Raw field for exact round-trip fidelity
- Private tags re-registered in PrivateCreatorDictionary from _private sub-document
- Tag key parsing supports Hex8 ("00100010"), Dotted ("0010.0010"), and Keyword ("PatientName") formats
- ToBson()/FromBson() convenience extension methods for fluent API

## Task Commits

Each task was committed atomically:

1. **Task 1: BsonDicomReader deserialization engine** - `c9508d1` (feat)
2. **Task 2: Extension methods for convenience API** - `656f932` (feat)

## Files Created/Modified

- `src/SharpDicom/Serialization/Bson/BsonDicomReader.cs` - BSON deserialization engine (970 lines)
- `src/SharpDicom/Serialization/Bson/DicomDatasetBsonExtensions.cs` - ToBson/FromBson convenience API (55 lines)

## Decisions Made

- Tag key parsing accepts all three BsonTagKeyFormat values (Hex8, Dotted, Keyword) regardless of serialization options, ensuring any serialized format can be deserialized
- Sequence items are captured as raw byte arrays during array parsing and recursively deserialized via DeserializeCore
- FromBson is a static method (not an extension) since there is no DicomDataset instance to extend; placed on extensions class for discoverability
- Flattened keys (from FlattenProfile) are detected by length/pattern and skipped during deserialization since they are informational

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- BsonDicomReader and extension methods compile on all 4 TFMs with zero warnings
- 4984 tests pass (0 failures, 183 skipped -- pre-existing skips)
- Round-trip capability complete: BsonDicomWriter.Serialize + BsonDicomReader.Deserialize
- Ready for Plan 03 (DICOM JSON) or Plan 04 (round-trip test suite)

---
*Phase: 29-mongodb-bson-serialization*
*Completed: 2026-02-07*
