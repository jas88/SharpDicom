# Phase 14: De-identification - Context

**Gathered:** 2026-02-02
**Status:** Ready for planning

<domain>
## Phase Boundary

Standards-compliant DICOM de-identification with full PS3.15 profile support, UID remapping, date shifting, burned-in PHI detection, and integration with existing element callback system. Users can apply configurable de-identification to single files or studies while maintaining referential integrity.

</domain>

<decisions>
## Implementation Decisions

### Profile Scope
- Full PS3.15 suite — Basic Profile plus all option profiles (Retain UIDs, Retain Patient Characteristics, Retain Device Identity, Clean Pixel Data, Clean Graphics, Clean Structured Content)
- Source-generated action table from NEMA part15.xml — extend existing dictionary generator
- Pluggable custom rules via IDeidentificationRule interface for extending/overriding standard behavior

### Burned-in PHI Detection
- Full detection with hybrid approach: OCR for text detection + heuristics for typical annotation regions
- Configurable replacement action: black, white, or average pixel value
- Apply same PHI detection to DICOM overlay planes (60xx groups)
- Scan text annotations in GSPS/SR for Clean Graphics profile — parse and redact PHI

### UID Handling
- Random UID generation (not deterministic) — maximum privacy, no correlation possible
- Study-level consistency via DeidentificationContext object with optional serialization to disk
- Configurable UID prefix, default to 2.25 (UUID-based) for globally unique IDs without registration
- Full traversal of all sequences for ReferencedSOPInstanceUID/ReferencedStudyInstanceUID remapping

### Date Handling
- Configurable date shifting strategy (per-patient, per-study, or per-element)
- Default: random shift within range (e.g., -365 to +365 days)
- Zero out time components (TM, DT) — keep shifted date, set time to 00:00:00
- Recalculate PatientAge (AS VR) from shifted birth date and study date

### API Design
- Both fluent builder and options object patterns
- Fluent: `DicomDeidentifier.Create().WithProfile(Basic).WithDateShift(-365, 365).Apply(dataset)`
- Options: `new DeidentificationOptions { ... }` for advanced scenarios
- Both low-level context passing and high-level study processor for multi-file operations
- Composable with existing ElementCallback system — de-id as callback in pipeline
- Full async API throughout for consistency with rest of library

### Claude's Discretion
- Exact OCR library choice (Tesseract or similar)
- Heuristic patterns for annotation region detection
- Internal threading/parallelization for batch processing
- Error handling strategy for malformed dates/UIDs

</decisions>

<specifics>
## Specific Ideas

- PS3.15 is the authoritative source — action table should be generated from official NEMA XML
- Privacy-first defaults: random UIDs, time zeroing, configurable but secure by default
- Integration with existing callback system enables combining de-id with validation in single pass

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 14-de-identification*
*Context gathered: 2026-02-02*
