# Phase 20: Critical Bug Fixes - Context

**Gathered:** 2026-02-02
**Status:** Ready for planning

<domain>
## Phase Boundary

Fix two known bugs blocking production use:
1. FindSequenceDelimiter parsing bug with deeply nested undefined-length sequences
2. Streaming C-STORE SCP parser not achieving full roundtrip fidelity

These are bug fixes, not new features. The reader and SCP already exist; they just need to work correctly.

</domain>

<decisions>
## Implementation Decisions

### FindSequenceDelimiter fix approach
- Use the 2 existing skipped roundtrip tests as primary reproduction cases
- Refactoring sequence parsing is acceptable if it makes the fix cleaner
- API changes allowed (public or internal) if needed for correctness
- Data model changes (DicomSequence/DicomDataset) acceptable — correctness over compatibility

### C-STORE SCP parser fix
- Enhance the streaming parser rather than switching to full DicomFileReader
- Must stay streaming — cannot buffer full dataset in memory
- Must handle both high-volume PACS receiver and testing scenarios
- IStreamingCStoreHandler API can change if needed for correctness

### Verification strategy
- FindSequenceDelimiter: skipped tests pass + byte-identical roundtrip + additional edge cases
- C-STORE SCP: aim for byte-identical roundtrip, semantic equivalence acceptable
- DCMTK interop testing required for both fixes
- Test data: existing corpus + real-world problem files + synthesized edge cases

### Regression prevention
- Comprehensive targeted test coverage + property-based testing (FsCheck-style)
- DCMTK interop tests run on every PR (required, blocking)
- Documentation in both code comments (explain the fix) and test descriptions (document the bug)
- Correctness first — performance optimization can come later if needed

### Claude's Discretion
- Specific refactoring approach for sequence parsing
- Property-based test generator design
- Exact structure of synthesized edge case files

</decisions>

<specifics>
## Specific Ideas

- The writer is correct; the bug is in the reader's FindSequenceDelimiter logic
- STATE.md notes: "Pre-existing reader bug in FindSequenceDelimiter"
- Streaming must work for high-volume PACS receivers (memory-constrained)

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 20-critical-bug-fixes*
*Context gathered: 2026-02-02*
