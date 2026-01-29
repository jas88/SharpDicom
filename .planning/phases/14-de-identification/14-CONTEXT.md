# Phase 14: De-identification - Context

**Gathered:** 2026-01-28
**Status:** Ready for planning

<domain>
## Phase Boundary

PS3.15 Basic Application Level Confidentiality Profile implementation with source-generated action tables from NEMA part15.xml, UID remapping with referential integrity, date shifting with temporal relationship preservation, and integration with element callback system. Includes Clean Pixel Data Option with region-based redaction.

</domain>

<decisions>
## Implementation Decisions

### Profile & Action Handling

- **All PS3.15 profiles supported**: Basic Profile plus all optional modules (Clean Pixel Data, Clean Descriptors, Clean Structured Content, Clean Graphics, Retain Device Identity, Retain Institution Identity, Retain Patient Characteristics, Retain Longitudinal Full Dates, Retain Safe Private)
- **Source generator from part15.xml**: Roslyn generator consumes NEMA XML at compile time
- **Full override allowed**: Users can override any action for any tag with custom rules
- **Configurable private tag default**: Let user choose keep/remove per de-identification run
- **Burned-in annotation with redaction**: Detect and optionally black out regions in pixel data
- **Region-based redaction**: Black out specific rectangular regions (manual or detected) for Clean Pixel Data
- **Clean Structured Content Option**: Apply PS3.15 structured content cleaning rules for SR
- **Configurable descriptor handling**: Let user choose Clean Descriptors or remove all per-run
- **Configurable graphics handling**: Let user choose Clean Graphics Option or remove all per-run
- **Remove unknown tags by default**: Unrecognized tags removed unless explicitly retained
- **Separate mapping file for reversibility**: Store original values in external JSON file, not in DICOM
- **Optional encryption for mapping file**: AES-256 encryption with user-provided key
- **Fail fast validation**: Validate all rules before processing any files
- **Mix and match profiles**: Apply multiple options/profiles together
- **Most restrictive wins**: When rules conflict, the most restrictive action applies
- **Common presets available**: Research, Clinical Trial, Teaching, etc. as starting points
- **Presets as templates**: Can be modified, not locked
- **Summary-only audit logging**: Log counts of actions by type
- **Both Deidentification Method tags**: Add (0012,0063) text and (0012,0064) Code Sequence
- **User choice for Patient Identity Removed**: Let user decide per-run whether to set (0012,0062)
- **Temporal flag set appropriately**: (0028,0303) set to MODIFIED or REMOVED based on actual action
- **Optional Clinical Trial attributes**: Allow user to provide trial info for (0012,00xx)
- **Both VR defaults and per-tag overrides**: For dummy replacement values
- **Full recursion into sequences**: Apply rules to all nested levels
- **Path-based context rules**: Rules can specify sequence context (e.g., ReferencedStudySequence/StudyInstanceUID)
- **Regex value patterns**: Rules can match on tag value
- **Group wildcards**: Rules can match gggg,* or *,eeee patterns
- **Creator-based private tag rules**: Rules can target specific private creators
- **VR wildcards**: Rules can target by VR type
- **Expression-based conditionals**: Rules can have conditions based on other tag values (full expression language)
- **JSON config file**: Save/load profiles as JSON
- **$extends inheritance**: Configs can extend other configs

### UID Remapping Strategy

- **Configurable scope**: Let user choose study-level or batch-level consistency
- **Optional persistence**: Can optionally persist mappings across sessions
- **SQLite database for mappings**: Queryable for large batches
- **UUID-derived UIDs (2.25.xxx)**: Standard UUID-based DICOM UID generation
- **All references remapped**: Find and remap all UID references in sequences
- **Configurable broken reference handling**: Let user choose generate/remove/error
- **Preserve standard UIDs**: Never remap Transfer Syntax, SOP Class, Coding Scheme, etc.
- **Configurable FrameOfReferenceUID**: Let user choose whether to remap
- **Detect and remap private tag UIDs**: Attempt to find UIDs in private data
- **Both VR and pattern detection**: VR-based (UI VR) plus regex scan as fallback
- **Scan text fields for UIDs**: Find and replace UIDs in text values like ImageComments
- **Preserve UID prefix structure**: Keep org root, replace suffix only
- **Related series share prefix**: Common prefix pattern for related series
- **Configurable Creator UID handling**: Let user choose remap/remove
- **Bidirectional lookup API**: Lookup original→new and new→original
- **Batch lookup/insert**: Efficient bulk operations for large datasets
- **Thread-safe concurrency**: Multiple threads can read/write mappings
- **Transactional operations**: Support commit/rollback for batch operations
- **Configurable TTL**: Mappings can expire after time period
- **Both JSON and SQL dump export**: Full database exportable in both formats

### Date/Time Shifting

- **Configurable strategy**: Let user choose fixed offset or random offset per patient
- **Preserve temporal order within study**: Same offset across dates within study preserves ordering
- **Configurable offset range**: User specifies min/max offset for random shifts
- **Configurable time handling**: Let user choose shift date only, shift datetime, or remove time

### API & Integration

- **Both fluent and options patterns**: Fluent builder and options object both supported
- **Callbacks with hooks**: Deidentifier as element callback plus before/after hooks for custom logic
- **Both streaming and loaded**: Support streaming de-identification and loaded dataset processing
- **Both directory and file list batch**: Built-in directory walker and file list processing

### Claude's Discretion

- Expression language implementation details
- Preset profile contents and naming
- Audit log format
- Region detection algorithm for burned-in annotations
- Default dummy values per VR
- SQLite schema design
- JSON config schema details
- Hook callback signatures

</decisions>

<specifics>
## Specific Ideas

- Source generator from part15.xml matches pattern from data dictionary generator
- SQLite for UID mappings enables efficient large-batch processing with transactions
- JSON config with $extends allows building org-specific profiles from standard bases
- Region-based redaction avoids OCR complexity while supporting manual region specification

</specifics>

<deferred>
## Deferred Ideas

- OCR-based burned-in text detection — complex ML dependency, region-based sufficient for now
- ICC color profile handling — deferred to future phase

</deferred>

---

*Phase: 14-de-identification*
*Context gathered: 2026-01-28*
