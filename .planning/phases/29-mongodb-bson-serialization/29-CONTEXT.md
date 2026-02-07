# Phase 29: MongoDB/BSON Serialization - Context

**Gathered:** 2026-02-07
**Status:** Ready for planning

<domain>
## Phase Boundary

Native BSON serialization for the "metadata in MongoDB, pixels on disk" architecture pattern. DicomDataset round-trips through BSON without losing fidelity. Core serializer produces raw BSON bytes with zero external dependencies; an optional adapter package provides MongoDB.Bson/MongoDB.Driver interop.

</domain>

<decisions>
## Implementation Decisions

### Tag key format
- Default output: 8-char hex (e.g., "00100010")
- Options for dotted group.element ("0010.0010") and keyword ("PatientName") output
- Accept all three input formats during deserialization

### Element structure
- Consistent sub-document format for ALL elements: `{"Value": [...], "Raw": "...", "vr": "PN"}`
- "Value" always a BsonArray (even for VM=1) for consistent query patterns
- "Raw" field present only for dual-storage VRs (IS, DS, DA, TM, DT) — contains original DICOM string
- "vr" field present only when ambiguous, private, or retired (option to always include)

### Numeric VRs (IS, DS)
- Dual storage: parsed BSON number (Int32/Int64 for IS, Double for DS) in "Value", original string in "Raw"
- Enables MongoDB range queries while preserving round-trip fidelity

### Date/Time VRs (DA, TM, DT)
- Dual storage: BSON DateTime (UTC) in "Value" for native date queries, original DICOM string in "Raw"
- Consistent with IS/DS dual-storage pattern

### Person Name (PN)
- Original DICOM string preserved (e.g., "Doe^John^^^")
- Parsed component fields added alongside for queryability (FamilyName, GivenName, etc.)
- Three component groups supported (Alphabetic, Ideographic, Phonetic)

### Binary data (OB, OW, OD, OF, OL, OV, UN)
- Size threshold: inline as BsonBinaryData below threshold, external reference above
- Default threshold: 16 KB (configurable)
- External references use structured format: `{"$ref": "gridfs", "id": ObjectId}` or `{"$ref": "file", "path": "..."}`
- Supports multiple backend types via structured reference

### DICOM-JSON compatibility
- Two output modes: MongoDB-native (default) and DICOM-JSON (PS3.18 Annex F)
- Selectable via serialization options
- MongoDB-native optimized for queries; DICOM-JSON for DICOMweb interoperability

### Sequence handling
- Default: nested BsonArray of sub-documents (natural BSON nesting)
- Configurable flattening for specific sequences (dot-notation fields)
- When flattened: both nested and flat fields coexist (nested for round-trip, flat for queries)
- Configurable depth limit, default 16 — beyond that, fall back to binary blob

### Flatten configuration
- Predefined profiles (e.g., "radiology", "pathology") with common sequences pre-selected
- Custom tag list for fine-grained control
- Both approaches combinable

### Private tag handling
- Grouped under `_private` sub-document, keyed by creator name: `{"_private": {"SIEMENS CT": {"00091001": ...}}}`
- Vendor dictionary names included when available (from Phase 6 vendor dictionaries)
- Configurable option to strip private tags during serialization
- Unknown-VR private tags stored as binary blob (BsonBinaryData)

### VR metadata
- Default: store VR only when ambiguous, private, or retired
- Option to always include VR for every element

### API surface
- Static serializer class (`BsonDicomSerializer`) as core engine
- Extension methods on `DicomDataset` for convenience (`dataset.ToBson()`, `DicomDataset.FromBson()`)
- Stream-based core (IBufferWriter<byte>) with byte[] convenience overloads

### Project structure
- Core BSON serializer in SharpDicom library (raw BSON writing, zero dependencies)
- Optional MongoDB.Bson adapter in separate package for BsonDocument/MongoDB.Driver interop

### Schema
- No schema version field in documents
- Schema evolution handled at application level

### Claude's Discretion
- Raw BSON writer implementation details (endianness handling, buffer management)
- Exact flattening profile contents for predefined profiles
- Internal buffer sizes and allocation strategies
- Test data generation approach

</decisions>

<specifics>
## Specific Ideas

- Architecture pattern: "metadata in MongoDB, pixels on disk" — SmiServices-inspired
- External binary references should support both file paths and GridFS
- Dual-storage pattern (parsed + raw) is the consistent approach for all queryable-but-lossy VRs
- BsonArray always (even VM=1) avoids query shape inconsistencies

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 29-mongodb-bson-serialization*
*Context gathered: 2026-02-07*
