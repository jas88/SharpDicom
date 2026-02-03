# SharpDicom Roadmap

## Milestones

- **v1.0.0 Core DICOM Toolkit** — Phases 1-9 (shipped 2026-01-28) — see [milestones/v1.0-ROADMAP.md](milestones/v1.0-ROADMAP.md)
- **v2.0.0 Network & Codecs** — Phases 10-14 (in progress)

---

## Milestone: v2.0.0 — Network, Codecs & De-identification

**Goal**: Full DICOM networking stack, pluggable image codecs, and standards-compliant de-identification

### Phase Overview

| Phase | Name | Requirements | Status |
|-------|------|--------------|--------|
| 10 | Network Foundation | FR-10.1, FR-10.2, FR-10.3, FR-10.4, FR-10.10, FR-10.11 | **Complete** |
| 11 | DIMSE Services | FR-10.5, FR-10.6, FR-10.7, FR-10.8, FR-10.9, FR-10.12 | Pending |
| 12 | Pure C# Codecs | FR-11.1, FR-11.2, FR-11.3, FR-11.4, FR-11.5, FR-11.6, FR-11.7 | Pending |
| 13 | Native Codecs Package | FR-12.1, FR-12.2, FR-12.3, FR-12.4, FR-12.5 | Pending |
| 14 | De-identification | FR-13.1, FR-13.2, FR-13.3, FR-13.4, FR-13.5, FR-13.6 | Pending |

---

## Phase 10: Network Foundation

**Goal**: Establish DICOM networking infrastructure with PDU handling, association negotiation, and basic connectivity verification

**Requirements**: FR-10.1, FR-10.2, FR-10.3, FR-10.4, FR-10.10, FR-10.11

**Plans**: 7 plans in 4 waves

Plans:
- [x] 10-01-PLAN.md — PDU types, constants, DicomStatus, network exceptions
- [x] 10-02-PLAN.md — Presentation context, user information, association options
- [x] 10-03-PLAN.md — PduReader/PduWriter ref structs for PDU I/O
- [x] 10-04-PLAN.md — DicomAssociation state machine (13 states)
- [x] 10-05-PLAN.md — DicomClient SCU with async connect/release
- [x] 10-06-PLAN.md — DicomServer SCP with event-based handlers
- [x] 10-07-PLAN.md — C-ECHO SCU/SCP and integration tests

**Must-haves**:
- [x] PDU parsing and building (A-ASSOCIATE-RQ/AC/RJ, P-DATA-TF, A-RELEASE-RQ/RP, A-ABORT)
- [x] PduReader/PduWriter ref structs following DicomStreamReader pattern
- [x] DicomAssociation state machine with presentation context negotiation
- [x] DicomClient class with async API for SCU operations
- [x] DicomServer class with event-based handlers for SCP operations
- [x] C-ECHO SCU implementation (verify remote connectivity)
- [x] C-ECHO SCP handler (respond to verification requests)

**Should-haves**:
- [x] Configurable ARTIM timer (association request/release timeout)
- [x] Configurable PDU size (16KB-1MB range)
- [x] Association abort with reason codes

**Dependencies**: Phase 4 (character encoding for DIMSE text VRs), Phase 7 (dataset serialization for command sets)

**Research Needed**: No (foundation layer; C-MOVE investigation deferred to Phase 11)

**Success Criteria**:
- [x] Can establish association with DCMTK storescp
- [x] Can accept association from DCMTK storescu
- [x] C-ECHO roundtrip succeeds (SCU and SCP)
- [x] PDU parsing handles fragmented reads correctly
- [x] Association state machine rejects malformed PDUs
- [x] Tests pass with real PACS simulator

---

## Phase 11: DIMSE Services

**Goal**: Complete DIMSE-C services for image storage, query, and retrieval operations

**Requirements**: FR-10.5, FR-10.6, FR-10.7, FR-10.8, FR-10.9, FR-10.12

**Plans**: 7 plans in 4 waves

Plans:
- [ ] 11-01-PLAN.md — Common DIMSE types (QueryRetrieveLevel, progress structs, DicomCommand extensions)
- [ ] 11-02-PLAN.md — CStoreScu service with streaming send and progress reporting
- [ ] 11-03-PLAN.md — CFindScu service with IAsyncEnumerable and DicomQuery builder
- [ ] 11-04-PLAN.md — C-STORE SCP handler support in DicomServer (buffered and streaming modes)
- [ ] 11-05-PLAN.md — CMoveScu service for third-party retrieval
- [ ] 11-06-PLAN.md — CGetScu service with inline C-STORE sub-operation handling
- [ ] 11-07-PLAN.md — Integration tests (roundtrip and DCMTK interoperability)

**Must-haves**:
- [ ] C-STORE SCU (send DICOM files to remote AE)
- [ ] C-STORE SCP with streaming support (receive without full buffering)
- [ ] C-FIND SCU (query PACS/RIS for studies/series/instances)
- [ ] C-MOVE SCU (retrieve from PACS via sub-operations)
- [ ] C-GET SCU (retrieve via C-STORE sub-ops on same association)
- [ ] IAsyncEnumerable for C-FIND/C-MOVE/C-GET responses

**Should-haves**:
- [ ] Zero-copy PDU parsing via System.IO.Pipelines
- [ ] Streaming C-STORE SCP handler with CopyToAsync pattern
- [ ] Element callback during network receive (validate/transform on arrival)
- [ ] Transfer syntax negotiation with transcoding capability

**Dependencies**: Phase 10 (association and PDU infrastructure)

**Research Needed**: Yes (C-MOVE third-party destination coordination, streaming receive patterns) - COMPLETE (11-RESEARCH.md)

**Success Criteria**:
- [ ] Can send DICOM file to DCMTK storescp
- [ ] Can receive DICOM file from DCMTK storescu
- [ ] C-FIND returns matching studies from test PACS
- [ ] C-MOVE triggers sub-operations to third-party destination
- [ ] C-GET retrieves instances directly
- [ ] Streaming receive does not buffer entire file in memory
- [ ] Association marked corrupted after PDU timeout (prevents data interleaving)

---

## Phase 12: Pure C# Codecs

**Goal**: JPEG and JPEG 2000 codecs implemented in pure C# for maximum portability and AOT compatibility

**Requirements**: FR-11.1, FR-11.2, FR-11.3, FR-11.4, FR-11.5, FR-11.6, FR-11.7

**Must-haves**:
- [ ] JPEG Baseline codec (8-bit lossy, Process 1 - TS 1.2.840.10008.1.2.4.50)
- [ ] JPEG Lossless codec (Process 14 SV1 - TS 1.2.840.10008.1.2.4.70)
- [ ] JPEG 2000 Lossless codec (TS 1.2.840.10008.1.2.4.90)
- [ ] Pure C# implementations with no native dependencies
- [ ] Trim/AOT compatible (no reflection, no dynamic code)
- [ ] Register via existing IPixelDataCodec interface and CodecRegistry

**Should-haves**:
- [ ] JPEG 2000 Lossy codec (TS 1.2.840.10008.1.2.4.91)
- [ ] Photometric interpretation handling (RGB/YBR conversion with metadata update)
- [ ] Multi-frame support with frame-level decode

**Dependencies**: Phase 9 (IPixelDataCodec interface and CodecRegistry)

**Research Needed**: No (well-documented JPEG/J2K specs, reference implementations available)

**Success Criteria**:
- [ ] Decode JPEG Baseline test files from NEMA WG-04 conformance suite
- [ ] Decode JPEG Lossless test files from NEMA WG-04 conformance suite
- [ ] Decode JPEG 2000 test files from NEMA WG-04 conformance suite (bit-perfect roundtrip)
- [ ] Encode to all supported transfer syntaxes
- [ ] Codecs discoverable via CodecRegistry.GetCodec(TransferSyntax)
- [ ] Passes AOT compilation test (no trimming warnings)
- [ ] Photometric Interpretation tag matches actual pixel data after encode

---

## Phase 13: Native Codecs Package

**Goal**: Optional high-performance codec package with native library wrappers for production workloads

**Requirements**: FR-12.1, FR-12.2, FR-12.3, FR-12.4, FR-12.5

**Plans**: 9 plans in 5 waves

Plans:
- [ ] 13-01-PLAN.md — Zig build system, C API header, version/feature detection, CI workflow
- [ ] 13-02-PLAN.md — libjpeg-turbo wrapper for JPEG baseline/extended (8-bit, 12-bit)
- [ ] 13-03-PLAN.md — OpenJPEG wrapper for JPEG 2000 with resolution level and ROI decode
- [ ] 13-04-PLAN.md — CharLS wrapper for JPEG-LS and FFmpeg wrapper for video codecs
- [ ] 13-05-PLAN.md — GPU acceleration with nvJPEG2000 and CPU fallback
- [ ] 13-06-PLAN.md — Managed P/Invoke layer, NativeCodecs static class, exception types
- [ ] 13-07-PLAN.md — IPixelDataCodec implementations and CodecRegistry priority integration
- [ ] 13-08-PLAN.md — NuGet package structure (meta + runtime packages for 6 RIDs)
- [ ] 13-09-PLAN.md — Test suite for native codec initialization and decode/encode operations

**Must-haves**:
- [ ] SharpDicom.Codecs NuGet package (separate from core)
- [ ] Native JPEG codec wrapping libjpeg-turbo (2-6x faster than pure C#)
- [ ] Native JPEG 2000 codec wrapping OpenJPEG
- [ ] Override registration that replaces pure C# codecs when loaded
- [ ] Cross-platform native binaries (win-x64, linux-x64, osx-arm64)

**Should-haves**:
- [ ] ModuleInitializer auto-registration on assembly load
- [ ] Fallback to pure C# if native load fails
- [ ] Native library version detection and logging

**Dependencies**: Phase 12 (establishes codec interface patterns)

**Research Needed**: Yes - COMPLETE (13-RESEARCH.md)

**Success Criteria**:
- [ ] SharpDicom.Codecs package installable via NuGet
- [ ] Native codecs auto-register when package referenced
- [ ] Native codecs override pure C# registrations
- [ ] Decode/encode works on Windows, Linux, macOS
- [ ] Performance benchmark shows 2-6x improvement over pure C# (note: NFR-05.2's 10-50x range is for specific workloads; typical improvement is 2-6x)
- [ ] Package does not bloat core SharpDicom library

---

## Phase 14: De-identification

**Goal**: Standards-compliant DICOM de-identification with PS3.15 Basic Profile and consistent UID/date handling

**Requirements**: FR-13.1, FR-13.2, FR-13.3, FR-13.4, FR-13.5, FR-13.6

**Plans**: 8 plans in 4 waves

Plans:
- [ ] 14-01-PLAN.md — Source generator for action tables from part15.xml
- [ ] 14-02-PLAN.md — Core types, action resolution, dummy value generation
- [ ] 14-03-PLAN.md — UID remapping with SQLite persistence
- [ ] 14-04-PLAN.md — Date/time shifting with configurable strategies
- [ ] 14-05-PLAN.md — DicomDeidentifier with fluent API and element callback
- [ ] 14-06-PLAN.md — JSON config format with $extends inheritance and presets
- [ ] 14-07-PLAN.md — Clean Pixel Data Option with region redaction
- [ ] 14-08-PLAN.md — Batch processing and integration tests


**Must-haves**:
- [ ] PS3.15 Basic Application Level Confidentiality Profile implementation
- [ ] Source-generated action table from NEMA part15.xml (extends dictionary generator)
- [ ] UID remapping with consistent study-level replacement (preserves referential integrity)
- [ ] Date shifting with configurable offset (preserves temporal relationships)
- [ ] Integration with existing element callback system (CallbackFilter, ElementCallback)
- [ ] DeidentificationContext for stateful remapping across multiple files

**Should-haves**:
- [ ] DicomDeidentifier class with fluent configuration API
- [ ] BurnedInPHIDetector warning for high-risk modalities (US, ES, SC, XA)
- [ ] Referenced SOP Instance UID updates in sequences (RT plans, presentation states)
- [ ] Safe private tag registry (preserve known-safe vendor tags)
- [ ] JSON config with $extends inheritance for org-specific profiles
- [ ] Clean Pixel Data Option with region-based redaction

**Dependencies**: Phase 4 (encoding for text element processing), Phase 7 (file writing for output)

**Research Needed**: Yes - COMPLETE (14-RESEARCH.md)

**Success Criteria**:
- [ ] Basic Profile removes all required tags per PS3.15 Annex E
- [ ] UID remapping maintains referential integrity across study
- [ ] Date shifting preserves temporal relationships within study
- [ ] De-identified files validate with DICOM validator
- [ ] Callback integration works with existing validation callbacks
- [ ] Warning raised for modalities with high burned-in PHI risk
- [ ] Roundtrip de-id -> re-id possible with mapping file

---

## Phase 18: Complete Managed Codec Implementations

**Goal**: Complete the managed (pure C#) implementations of JPEG-LS and HTJ2K codecs for full AOT/trimming compatibility without native dependencies

**Requirements**: Extension of FR-11 (Pure C# codecs)

**Must-haves**:
- [ ] Complete JPEG-LS encoder/decoder (ITU-T T.87 / ISO/IEC 14495-1)
  - [ ] Lossless mode (NEAR=0)
  - [ ] Near-lossless mode (NEAR>0)
  - [ ] Context modeling and Golomb-Rice coding
  - [ ] Proper bounds checking and error handling
- [ ] Complete JPEG 2000 encoder/decoder for HTJ2K support
  - [ ] Wavelet transform (5/3 reversible, 9/7 irreversible)
  - [ ] EBCOT tier-1/tier-2 coding
  - [ ] Proper progression order support (LRCP, RPCL)
- [ ] Roundtrip encode/decode tests passing for all bit depths

**Should-haves**:
- [ ] Performance optimization (SIMD where applicable)
- [ ] Multi-threaded encoding for large images

**Dependencies**: Phase 12 (codec interface), Phase 17 (codec infrastructure scaffolding)

**Research Needed**: Yes (JPEG-LS algorithm details, J2K tier-1 coding)

**Success Criteria**:
- [ ] JPEG-LS roundtrip tests pass (currently skipped)
- [ ] HTJ2K roundtrip tests pass (currently skipped)
- [ ] No native library dependencies required
- [ ] AOT/trimming compatible
- [ ] Performance within 10x of native implementations (acceptable for portability trade-off)

---

## Critical Path

```
v1.0.0 Complete (Phases 1-9)
         |
         v
Phase 10 (Network Foundation)
         |
         v
Phase 11 (DIMSE Services)
         |
         +------------------------+
         |                        |
         v                        v
Phase 12 (Pure Codecs)      Phase 14 (De-id)
         |
         v
Phase 13 (Native Codecs)*
         |
         +------------------------+
                                  |
                                  v
                            v2.0.0 Release

* Phase 13 depends on Phase 12 for codec interface patterns
```

**Parallelization opportunities**:
- Phase 12 (Pure Codecs) and Phase 14 (De-identification) can run in parallel
- Phase 13 must follow Phase 12 (needs codec interface patterns)
- Networking phases (10, 11) are sequential

**Estimated effort**:
- Phase 10: 5-7 days (PDU/Association/C-ECHO)
- Phase 11: 8-10 days (C-STORE/FIND/MOVE/GET with streaming)
- Phase 12: 6-8 days (three codec implementations)
- Phase 13: 4-5 days (native wrappers, packaging)
- Phase 14: 5-6 days (de-id profiles, UID remapping)
- **Total**: 28-36 days (with parallelization: 20-26 days)

---

*Last updated: 2026-02-02 (v1.0.0 milestone archived)*
