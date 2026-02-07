---
phase: 29-mongodb-bson-serialization
plan: 01
subsystem: serialization
tags: [bson, mongodb, serialization, dicom-dataset, binary-protocol]

# Dependency graph
requires:
  - phase: 01-core-types
    provides: DicomTag, DicomVR, DicomVRInfo, DicomDataset, element types
  - phase: 06-dataset
    provides: DicomDataset, PrivateCreatorDictionary, DicomDictionary
provides:
  - BsonType constants for BSON protocol
  - BsonDocumentBuffer for deferred-size BSON writing with ArrayPool
  - BsonSerializationOptions for configurable serialization
  - BsonDicomWriter.Serialize for DicomDataset-to-BSON conversion
  - BinaryDataReference for external binary storage references
  - FlattenProfile for sequence flattening configuration
affects: [29-02-bson-deserialization, 29-03-dicom-json, 29-04-mongodb-integration]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Deferred-size BSON document pattern: BeginDocument/EndDocument with PatchInt32At"
    - "VR-based element dispatch for type-specific BSON encoding"
    - "Dual-storage pattern: parsed Value array + original Raw string for IS/DS/DA/TM/DT"
    - "Private tag grouping under _private sub-document keyed by creator name"

key-files:
  created:
    - src/SharpDicom/Serialization/Bson/BsonType.cs
    - src/SharpDicom/Serialization/Bson/BsonTagKeyFormat.cs
    - src/SharpDicom/Serialization/Bson/BsonOutputMode.cs
    - src/SharpDicom/Serialization/Bson/BinaryDataReference.cs
    - src/SharpDicom/Serialization/Bson/FlattenProfile.cs
    - src/SharpDicom/Serialization/Bson/BsonSerializationOptions.cs
    - src/SharpDicom/Serialization/Bson/BsonDocumentBuffer.cs
    - src/SharpDicom/Serialization/Bson/BsonDicomWriter.cs
  modified: []

key-decisions:
  - "Used ArgumentOutOfRangeException.ThrowIfNegative with #if NET8_0_OR_GREATER guard for analyzer compliance"
  - "SV/UV VRs handled via raw byte access since DicomNumericElement lacks Int64/UInt64 accessors"
  - "Flatten profile writes only first sequence item fields as dot-notation (common single-item pattern)"
  - "Zero external dependencies: pure BinaryPrimitives + UTF8 encoding, no MongoDB driver needed"

patterns-established:
  - "Serialization/Bson namespace for all BSON-related types"
  - "BsonDocumentBuffer as reusable internal buffer for BSON document construction"
  - "Static BsonDicomWriter.Serialize as entry point for dataset serialization"

# Metrics
duration: 6min
completed: 2026-02-07
---

# Phase 29 Plan 01: Core BSON Serialization Summary

**Zero-dependency BSON serializer with deferred-size document buffer, dual-storage for IS/DS/DA/TM/DT, PN component parsing, and private tag grouping**

## Performance

- **Duration:** 6 min
- **Started:** 2026-02-07T19:14:00Z
- **Completed:** 2026-02-07T19:20:11Z
- **Tasks:** 2
- **Files created:** 8

## Accomplishments

- Complete BSON serialization of DicomDataset to raw bytes with zero external dependencies
- Dual-storage for IS (Int32/Int64 + Raw), DS (Double + Raw), DA/DT (DateTime + Raw), TM (Int64 ms + Raw)
- Person Name (PN) parsing into Alphabetic/Ideographic/Phonetic component groups with 5-part field breakdown
- Private tag grouping under `_private` sub-document keyed by creator name
- Binary threshold with external handler callback for large binary data (GridFS, file)
- Sequence nesting with configurable MaxSequenceDepth guard
- Flatten profile support for denormalized MongoDB queries
- ArrayPool-backed BsonDocumentBuffer with deferred-size document pattern

## Task Commits

Each task was committed atomically:

1. **Task 1: Core BSON serialization types and BsonDocumentBuffer** - `20eb02c` (feat)
2. **Task 2: BsonDicomWriter serialization engine** - `b7170e2` (feat)

## Files Created/Modified

- `src/SharpDicom/Serialization/Bson/BsonType.cs` - BSON type code constants
- `src/SharpDicom/Serialization/Bson/BsonTagKeyFormat.cs` - Tag key format enum (Hex8/Dotted/Keyword)
- `src/SharpDicom/Serialization/Bson/BsonOutputMode.cs` - Output mode enum (MongoNative/DicomJson)
- `src/SharpDicom/Serialization/Bson/BinaryDataReference.cs` - External binary reference with GridFS/File factories
- `src/SharpDicom/Serialization/Bson/FlattenProfile.cs` - Sequence flattening configuration with radiology preset
- `src/SharpDicom/Serialization/Bson/BsonSerializationOptions.cs` - Configurable serialization options
- `src/SharpDicom/Serialization/Bson/BsonDocumentBuffer.cs` - Deferred-size BSON document buffer with ArrayPool
- `src/SharpDicom/Serialization/Bson/BsonDicomWriter.cs` - Main serialization engine (998 lines)

## Decisions Made

- Used `#if NET8_0_OR_GREATER` guard for `ArgumentOutOfRangeException.ThrowIfNegative` to satisfy CA1512 analyzer on modern TFMs while maintaining netstandard2.0 compatibility
- SV/UV VRs are not yet used by DicomNumericElement (no Int64/UInt64 accessors) -- handled via raw byte fallback in binary element path
- Flatten profile writes only the first sequence item's fields as dot-notation keys (single-item sequences are the common case in radiology)
- No external dependencies added -- all BSON encoding uses BinaryPrimitives and System.Text.Encoding.UTF8

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] CA1512 analyzer error for ArgumentOutOfRangeException**
- **Found during:** Task 1
- **Issue:** `throw new ArgumentOutOfRangeException` triggers CA1512 on net8.0+ which requires `ThrowIfNegative`
- **Fix:** Added `#if NET8_0_OR_GREATER` conditional to use the preferred API on modern TFMs
- **Files modified:** src/SharpDicom/Serialization/Bson/BsonDocumentBuffer.cs
- **Verification:** Build passes on all 4 TFMs with 0 warnings
- **Committed in:** 20eb02c (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Necessary for zero-warning compilation. No scope creep.

## Issues Encountered

None

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- All 8 source files created and compiling on netstandard2.0, net8.0, net9.0, net10.0
- 4984 tests pass (0 failures, 183 skipped -- pre-existing skips)
- Ready for 29-02 (BSON deserialization) which will consume BsonDicomWriter output

---
*Phase: 29-mongodb-bson-serialization*
*Completed: 2026-02-07*
