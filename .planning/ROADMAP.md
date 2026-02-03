# SharpDicom Roadmap

## Milestones

- **v1.0.0 Core DICOM Toolkit** — Phases 1-9 (shipped 2026-01-28) — see [milestones/v1.0-ROADMAP.md](milestones/v1.0-ROADMAP.md)
- **v2.0.0 Network, Codecs & De-identification** — Phases 10-14 (shipped 2026-02-02) — see [milestones/v2.0-ROADMAP.md](milestones/v2.0-ROADMAP.md)
- **v3.0.0 Polish, CLI & Migration** — Phases 20-29 (planned)

---

## Milestone: v3.0.0 — Polish, CLI & Migration

**Goal**: Fix known issues, complete codec implementations, add CLI tools, and provide fo-dicom migration path

### Phase Overview

| Phase | Name | Priority | Status |
|-------|------|----------|--------|
| 20 | Critical Bug Fixes | **URGENT** | Pending |
| 21 | Complete Managed Codecs | High | Pending |
| 22 | TLS Networking | High | Pending |
| 23 | CLI Tools (sharpdcm) | High | Pending |
| 24 | Server-Side DIMSE | Medium | Pending |
| 25 | Advanced De-identification | Medium | Pending |
| 26 | Migration Tooling | Medium | Pending |
| 27 | Extended Codec Support | Low | Pending |
| 28 | DIMSE-N Services | Low | Pending |
| 29 | MongoDB/BSON Serialization | Medium | Pending |

---

## Phase 20: Critical Bug Fixes (URGENT)

**Goal**: Fix known bugs blocking production use

**Priority**: URGENT — Must be first phase of v3.0

**Must-haves**:
- [ ] Fix FindSequenceDelimiter for deeply nested undefined-length sequences
  - Reader bug causes roundtrip failures with undefined-length nested sequences
  - Writer is correct; reader parsing logic needs fix
  - Currently causes 2 skipped roundtrip tests
- [ ] Fix streaming C-STORE SCP parser for full roundtrip fidelity
  - Simplified parser doesn't preserve all elements perfectly
  - Need full DicomFileReader integration for server-side receive

**Success Criteria**:
- [ ] All previously skipped undefined-length sequence tests pass
- [ ] C-STORE SCP roundtrip produces byte-identical datasets

---

## Phase 21: Complete Managed Codecs

**Goal**: Complete pure C# JPEG-LS and HTJ2K codecs (infrastructure added in v2.0)

**Must-haves**:
- [ ] Complete JPEG-LS encoder/decoder (ITU-T T.87 / ISO/IEC 14495-1)
  - [ ] Lossless mode (NEAR=0)
  - [ ] Near-lossless mode (NEAR>0)
  - [ ] Context modeling and Golomb-Rice coding
  - [ ] Proper bounds checking and error handling
- [ ] Complete HTJ2K encoder/decoder
  - [ ] High-Throughput JPEG 2000 (ISO/IEC 15444-15)
  - [ ] Block coder optimization
- [ ] Roundtrip encode/decode tests passing for all bit depths

**Should-haves**:
- [ ] SIMD optimization where applicable
- [ ] Multi-threaded encoding for large images

**Success Criteria**:
- [ ] JPEG-LS roundtrip tests pass (currently skipped)
- [ ] HTJ2K roundtrip tests pass (currently skipped)
- [ ] Performance within 10x of native implementations

---

## Phase 22: TLS Networking

**Goal**: Secure DICOM networking with TLS support

**Must-haves**:
- [ ] TLS 1.2/1.3 support for DicomClient
- [ ] TLS 1.2/1.3 support for DicomServer
- [ ] Certificate validation options (system store, custom CA, self-signed)
- [ ] Client certificate authentication

**Should-haves**:
- [ ] Certificate pinning option
- [ ] DICOM TLS connection profile conformance

**Success Criteria**:
- [ ] Secure connection to DCMTK with TLS
- [ ] Mutual TLS authentication working

---

## Phase 23: CLI Tools (sharpdcm)

**Goal**: Comprehensive command-line toolkit as single binary with subcommands

**Must-haves**:
- [ ] `sharpdcm` unified CLI with subcommands
- [ ] `sharpdcm dump` — Display DICOM file contents (dcmdump equivalent)
  - [ ] Tag display with names and values
  - [ ] Configurable output format (text, JSON, XML)
  - [ ] Sequence depth limiting
  - [ ] Private tag display with vendor names
- [ ] `sharpdcm store` — Send DICOM files (storescu equivalent)
  - [ ] Single file and directory send
  - [ ] Progress reporting
  - [ ] Retry on failure
- [ ] `sharpdcm find` — Query DICOM server (findscu equivalent)
  - [ ] Patient/Study/Series/Instance level queries
  - [ ] Output as text, JSON, or CSV
- [ ] `sharpdcm lint` — Validate DICOM files (dcmlint)
  - [ ] Strict/Lenient/Permissive profiles
  - [ ] Machine-readable output (JSON)
  - [ ] Exit codes for CI integration
- [ ] `sharpdcm fix` — Repair common DICOM issues (dcmfix)
  - [ ] Fix invalid UIDs
  - [ ] Fix invalid dates/times
  - [ ] Fix character encoding issues
  - [ ] Remove invalid elements
  - [ ] Dry-run mode

**Should-haves**:
- [ ] `sharpdcm move` — Retrieve via C-MOVE (movescu equivalent)
- [ ] `sharpdcm get` — Retrieve via C-GET (getscu equivalent)
- [ ] `sharpdcm echo` — Verify connectivity (echoscu equivalent)
- [ ] `sharpdcm deid` — De-identify files
- [ ] `sharpdcm convert` — Transcode between transfer syntaxes
- [ ] Shell completion (bash, zsh, PowerShell)
- [ ] Single-file deployment (AOT compiled)

**Success Criteria**:
- [ ] All subcommands functional
- [ ] AOT compilation produces single executable
- [ ] Works on Windows, Linux, macOS

---

## Phase 24: Server-Side DIMSE (SCP)

**Goal**: Complete server-side query/retrieve implementation

**Must-haves**:
- [ ] C-FIND SCP — Respond to queries
  - [ ] Patient/Study/Series/Instance level
  - [ ] Pluggable data source interface
- [ ] C-MOVE SCP — Handle retrieve requests
  - [ ] Forward to third-party destination
  - [ ] Sub-operation tracking

**Should-haves**:
- [ ] C-GET SCP — Respond to C-GET requests
- [ ] Query result pagination for large datasets

**Success Criteria**:
- [ ] Can serve as mini-PACS for testing
- [ ] DCMTK findscu/movescu work against SharpDicom SCP

---

## Phase 25: Advanced De-identification

**Goal**: Enhanced de-identification capabilities

**Must-haves**:
- [ ] OCR-based burned-in PHI detection (Tesseract integration)
  - [ ] Detect text regions in pixel data
  - [ ] Configurable confidence threshold
  - [ ] Region reporting for manual review
- [ ] Referenced SOP Instance UID updates in sequences
  - [ ] RT Plan references
  - [ ] Presentation State references
  - [ ] Structured Report references

**Should-haves**:
- [ ] Additional de-identification profiles
  - [ ] Retain longitudinal temporal information
  - [ ] Retain modified dates
  - [ ] Clean structured content
- [ ] Re-identification support with mapping file

**Success Criteria**:
- [ ] OCR detects burned-in text in test images
- [ ] RT Plan de-identification maintains referential integrity

---

## Phase 26: Migration Tooling

**Goal**: Provide migration path from fo-dicom

**Must-haves**:
- [ ] SharpDicom.FoDicom.Compat — Adapter layer
  - [ ] DicomFile compatibility shim
  - [ ] DicomDataset API mapping
  - [ ] DicomClient/DicomServer adapters
  - [ ] Common extension method equivalents
- [ ] SharpDicom.Analyzers — Roslyn analyzer
  - [ ] Detect fo-dicom API usage
  - [ ] Suggest SharpDicom equivalents
  - [ ] Code fix providers for automated migration

**Should-haves**:
- [ ] Migration guide documentation
- [ ] Benchmark comparisons (performance, memory)

**Migration targets** (in order):
1. dcm2csv
2. nccid
3. SmiServices
4. RdmpDicom

**Success Criteria**:
- [ ] dcm2csv migrated and passing tests
- [ ] Analyzer detects 90%+ of fo-dicom patterns

---

## Phase 27: Extended Codec Support

**Goal**: Additional codec capabilities

**Should-haves**:
- [ ] 12-bit JPEG support (libjpeg-turbo with WITH_12BIT)
- [ ] MPEG2 encoding (currently decode-only)
- [ ] H.264/HEVC encoding for video DICOM

**Success Criteria**:
- [ ] 12-bit JPEG roundtrip works
- [ ] Can create video DICOM from frame sequence

---

## Phase 28: DIMSE-N Services

**Goal**: Normalized object services (low priority, <5% use cases)

**Should-haves**:
- [ ] N-CREATE, N-SET, N-GET, N-DELETE, N-ACTION, N-EVENT-REPORT
- [ ] Modality Performed Procedure Step (MPPS)
- [ ] Storage Commitment

**Success Criteria**:
- [ ] MPPS workflow functional

---

## Phase 29: MongoDB/BSON Serialization

**Goal**: Native MongoDB/BSON serialization for the metadata → MongoDB, pixels → disk architecture pattern

**Must-haves**:
- [ ] DicomDataset → BsonDocument serialization (in core library)
- [ ] BsonDocument → DicomDataset deserialization
- [ ] Streaming serialization (avoid full materialization)
- [ ] Private tag preservation
- [ ] Sequence flattening options for query optimization

**Should-haves**:
- [ ] MongoDB.Driver integration helpers
- [ ] Index recommendations for common query patterns
- [ ] Bulk import/export utilities

**Success Criteria**:
- [ ] Roundtrip serialization maintains all DICOM elements
- [ ] Performance comparable to direct BSON serialization
- [ ] SmiServices integration path documented

---

## v4.0.0+ Future Vision

| Feature | Notes |
|---------|-------|
| PACS federation daemon | Usenet-style push/pull redundancy |
| Web DICOM viewer | DICOMweb + browser-based viewer |
| Cloud storage backends | S3, Azure Blob, GCS for pixel data |

---

*Last updated: 2026-02-02 (v3.0.0 scope defined)*
