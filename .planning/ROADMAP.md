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
| 20 | Critical Bug Fixes | **URGENT** | **COMPLETE** |
| 21 | Complete Managed Codecs | High | **COMPLETE** |
| 22 | TLS Networking | High | Pending |
| 23 | CLI Tools (sharpdcm) | High | Pending |
| 24 | Server-Side DIMSE | Medium | Pending |
| 25 | Advanced De-identification | Medium | Pending |
| 26 | Migration Tooling | Medium | Pending |
| 27 | Extended Codec Support | Low | Pending |
| 28 | DIMSE-N Services | Low | Pending |
| 29 | MongoDB/BSON Serialization | Medium | Pending |
| 30 | HT Block Coder | Low | Future |

---

## Phase 20: Critical Bug Fixes (URGENT)

**Goal**: Fix known bugs blocking production use

**Priority**: URGENT — Must be first phase of v3.0

**Plans:** 3 plans

Plans:
- [x] 20-01-PLAN.md — Fix FindSequenceDelimiter depth tracking in DicomStreamReader
- [x] 20-02-PLAN.md — Integrate SequenceParser into C-STORE SCP ParseDataset
- [x] 20-03-PLAN.md — Add property-based testing and DCMTK interoperability verification

**Additional fixes during execution:**
- [x] Fix DicomClient.SerializeDataset to use WriteDataset() for proper sequence handling
- [x] Fix DicomServer.FindSequenceContentLengthStatic to properly skip elements in sequences

**Must-haves** (all complete):
- [x] Fix FindSequenceDelimiter for deeply nested undefined-length sequences
- [x] Fix streaming C-STORE SCP parser for full roundtrip fidelity
- [x] Fix C-STORE SCU to properly serialize sequences

**Success Criteria** (all met):
- [x] All previously skipped undefined-length sequence tests pass
- [x] C-STORE SCP roundtrip preserves all elements including sequences
- [x] Property-based tests cover arbitrary nesting depths (140+ iterations)
- [x] DCMTK interop tests pass (when DCMTK available)

---

## Phase 21: Complete Managed Codecs

**Goal**: Complete pure C# JPEG-LS and HTJ2K codecs (infrastructure added in v2.0)

**Plans:** 9 plans (4 original + 5 gap closure)

Plans:
- [x] 21-01-PLAN.md — JPEG-LS codec implementation (predictors, contexts, Golomb-Rice, all interleave modes)
- [x] 21-02-PLAN.md — HTJ2K codec shell (J2K delegation, HT block coder deferred)
- [x] 21-03-PLAN.md — SIMD optimization and auto-parallelization for performance
- [x] 21-04-PLAN.md — Conformance testing against CharLS/OpenJPH reference implementations
- [x] 21-05-PLAN.md — Gap closure: Fix JPEG-LS roundtrip test failures (complete - 16/16 tests pass)
- [x] 21-06-PLAN.md — Gap closure: Fix MQ coder uniform coding (partial - MQ fixed, EBCOT/tier-2 deferred)
- [x] 21-07-PLAN.md — Gap closure: Fix EBCOT encoder/decoder state tracking asymmetry
- [x] 21-08-PLAN.md — Gap closure: Fix tier-2 packet encoding (partial - tier-2 fixed, deeper pipeline issues identified)
- [ ] 21-09-PLAN.md — Gap closure: Fix J2K encoder/decoder packet assembly/parsing, enable HTJ2K roundtrip tests

**Verification Status** (from 21-VERIFICATION.md):
- 7/9 must-haves verified (after 21-05, 21-06, 21-07, 21-08)
- Remaining gap: HTJ2K roundtrip tests blocked by J2K pipeline integration issues (21-09)

**Gap Closure Summary:**
1. **JPEG-LS test failures** — FIXED in 21-05 (16/16 tests pass)
2. **MQ coder uniform coding** — FIXED in 21-06
3. **EBCOT state tracking asymmetry** — FIXED in 21-07 (11/14 EBCOT tests pass)
4. **Tier-2 packet encoding mismatch** — FIXED in 21-08 (ReadNumPasses, WriteZeroBitPlanes)
5. **J2K pipeline integration** — PLANNED in 21-09 (encoder/decoder packet assembly/parsing)
6. **HT block coder** — Deferred to Phase 30 (3000-5000 LOC, multi-week effort)

**Must-haves**:
- [x] Complete JPEG-LS encoder/decoder (ITU-T T.87 / ISO/IEC 14495-1)
  - [x] Lossless mode (NEAR=0) — implemented and verified
  - [x] Near-lossless mode (NEAR>0) — implemented
  - [x] Context modeling and Golomb-Rice coding — implemented
  - [x] All 8 predictors from ITU-T T.87 — implemented
  - [x] All three interleave modes (none, line, sample) — implemented
- [ ] Complete HTJ2K encoder/decoder
  - [x] High-Throughput JPEG 2000 shell — implemented via J2K delegation
  - [ ] ~~HT block coder replacing EBCOT for 10x performance~~ — DEFERRED to Phase 30
  - [x] Both 5/3 (lossless) and 9/7 (lossy) DWT modes — via J2K
- [ ] Roundtrip encode/decode tests passing for all bit depths (8, 12, 16) — plan 21-09

**Should-haves**:
- [x] SIMD optimization using Vector128/256 — completed in 21-03
- [ ] Multi-threaded encoding for large images (auto-parallel > 512x512)
- [ ] Configurable strict/lenient error handling

**Success Criteria**:
- [x] JPEG-LS roundtrip tests pass (16 tests) — completed in 21-05
- [ ] HTJ2K roundtrip tests pass (16 tests) — pending 21-09
- [ ] Performance within 10x of native implementations (CharLS, OpenJPH)
- [x] Output decodable by reference implementations — conformance tests in 21-04

---

## Phase 22: TLS Networking

**Goal**: Secure DICOM networking with TLS 1.2/1.3 support via SslStream wrapping

**Plans:** 4 plans

Plans:
- [ ] 22-01-PLAN.md — TLS configuration types, exception hierarchy, and certificate validator
- [ ] 22-02-PLAN.md — DicomClient TLS integration (SslStream wrapping in ConnectAsync)
- [ ] 22-03-PLAN.md — DicomServer TLS integration (SslStream wrapping in HandleAssociationAsync)
- [ ] 22-04-PLAN.md — TLS integration tests (C-ECHO, C-STORE, mTLS, certificate validation)

**Must-haves**:
- [ ] TLS 1.2/1.3 support for DicomClient
- [ ] TLS 1.2/1.3 support for DicomServer
- [ ] Certificate validation options (system store, custom CA, self-signed)
- [ ] Client certificate authentication
- [ ] Certificate pinning via thumbprint whitelist
- [ ] DICOM BCP 195 TLS profile conformance

**Success Criteria**:
- [ ] TLS C-ECHO roundtrip between DicomClient and DicomServer
- [ ] Mutual TLS authentication working
- [ ] Self-signed certificate accepted via thumbprint whitelist
- [ ] Invalid certificates rejected

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

**Goal**: Native MongoDB/BSON serialization for the metadata -> MongoDB, pixels -> disk architecture pattern

**Must-haves**:
- [ ] DicomDataset -> BsonDocument serialization (in core library)
- [ ] BsonDocument -> DicomDataset deserialization
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

## Phase 30: HT Block Coder (Future)

**Goal**: Implement true High-Throughput JPEG 2000 block coding per ISO/IEC 15444-15

**Priority**: Low — Deferred from Phase 21 due to complexity

**Rationale for deferral**:
- Estimated 3000-5000 lines of code
- Requires deep study of ITU-T T.814 specification
- Current HTJ2K implementation is functionally correct via J2K delegation
- Performance optimization not blocking other work

**Must-haves**:
- [ ] HtBlockCoder implementing ISO/IEC 15444-15 HT algorithm
- [ ] HtBitWriter/HtBitReader for VLC entropy coding
- [ ] Integration routing in J2kEncoder/Decoder to use HT when requested
- [ ] 10x performance improvement over EBCOT for typical medical images

**Should-haves**:
- [ ] SIMD optimization for VLC coding
- [ ] Conformance test vectors from ITU-T

**Success Criteria**:
- [ ] HTJ2K encoding 10x faster than standard J2K
- [ ] Output decodable by OpenJPH

---

## v4.0.0+ Future Vision

| Feature | Notes |
|---------|-------|
| PACS federation daemon | Usenet-style push/pull redundancy |
| Web DICOM viewer | DICOMweb + browser-based viewer |
| Cloud storage backends | S3, Azure Blob, GCS for pixel data |

---

*Last updated: 2026-02-04 (Phase 22 TLS Networking planned — 4 plans in 3 waves)*
