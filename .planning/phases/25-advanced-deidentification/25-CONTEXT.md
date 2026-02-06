# Phase 25: Advanced De-identification - Context

**Gathered:** 2026-02-06
**Status:** Ready for planning

<domain>
## Phase Boundary

Enhanced de-identification capabilities beyond the PS3.15 Basic Profile implemented in Phase 14. Two deliverables: (1) OCR-based burned-in PHI detection using Tesseract native bindings, and (2) referenced SOP Instance UID updates across all known referencing SOP classes. Does NOT include new de-identification profiles, re-identification support, or new CLI commands.

</domain>

<decisions>
## Implementation Decisions

### OCR Detection Scope
- Both detect-only and auto-redact APIs — caller chooses mode
- Default scan modalities: High + Moderate risk (US, ES, SC, XA, MG). Configurable.
- Multi-frame images: scan every frame (burned-in text may appear mid-clip)
- Flag all detected text, then filter with allow/deny logic
- Built-in allowlist of common non-PHI labels (measurement units, orientation markers, etc.) as default; user can extend or replace
- Region-aware confidence thresholds: lower threshold for corners/edges (where burned-in text typically lives), higher for center
- In-place pixel modification for uncompressed data (black rectangle)
- Compressed pixel data: configurable — either decompress and store lossless (safe default), or re-compress with same algorithm (caller opts in, accepting generation loss)
- OCR results include detected text string, confidence score, and bounding rectangle per region (enables content-based allow/deny filtering)

### Tesseract Integration
- P/Invoke native binding following Phase 13 pattern (Zig cross-compilation)
- Bundled as part of native .Codecs package (SharpDicom.Codecs)
- Fail fast if OCR is requested but Tesseract native library isn't available (no silent degradation)
- Bundle English language data (eng, ~12MB). Users add other languages by providing traineddata path.

### UID Reference Updates
- Cover all known referencing SOP classes: RT Plan, Presentation State, Structured Report, Key Object Selection, Encapsulated PDF references, Registration objects, Waveform references
- Remap from persistent UidRemapper store (Phase 14 SQLite/InMemory). If UID was seen before in any batch, use stored mapping.
- Unlimited depth sequence traversal — walk all sequences recursively, remap every UI VR element that matches a known UID pattern
- Remap ALL reference UIDs: SOPInstanceUID, ReferencedSOPInstanceUID, FrameOfReferenceUID, SynchronizationFrameOfReferenceUID, and all spatial reference UIDs for complete referential integrity

### API Surface Design
- OcrScanner as standalone class + DicomDeidentifierBuilder.WithOcrScanner() integration
- UidReferenceWalker as standalone class + DicomDeidentifier pipeline integration
- Options object pattern for OCR configuration (OcrScannerOptions), consistent with existing Phase 14 patterns
- OCR results: text + bounding boxes + confidence per region

### Claude's Discretion
- Exact allow/deny list contents for built-in non-PHI labels
- Region boundary definitions for corner/edge vs center threshold zones
- Specific Tesseract API calls and page segmentation mode
- UidReferenceWalker internal traversal algorithm
- Test image generation for OCR verification

</decisions>

<specifics>
## Specific Ideas

- Phase 14's BurnedInAnnotationDetector already categorizes modality risk (High: US/ES/SC, Moderate: XA/MG, Low: CT/MR) — reuse this categorization for default scan modality selection
- Phase 14's UidRemapper (InMemoryUidStore, SqliteUidStore) already handles persistent UID mapping — reference walker should consume this directly
- Phase 13 native codec pattern (Zig cross-compilation, RID-specific NuGet packages, P/Invoke with LibraryImport on NET7+) is the template for Tesseract integration
- **Known risk**: Zig cross-compilation from Linux to macOS failed for a different project (missing library issues). Researcher must verify whether Tesseract + Leptonica can be cross-compiled to macOS targets, or whether macOS builds require native CI runners. Consider minimal Leptonica build (disable TIFF/PNG deps since we feed raw pixel buffers, not files) to reduce cross-compilation surface.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 25-advanced-deidentification*
*Context gathered: 2026-02-06*
