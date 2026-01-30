# SharpDicom Roadmap

## Milestone: v1.0.0 — Core DICOM Toolkit

**Goal**: Production-ready DICOM file I/O with streaming architecture

### Phase Overview

| Phase | Name | Requirements | Status |
|-------|------|--------------|--------|
| 1 | Core Data Model & Dictionary | FR-01, FR-02 | **Complete** |
| 2 | Basic File Reading | FR-03.1-3.3, FR-03.6 | **Complete** |
| 3 | Implicit VR & Sequences | FR-03.4, FR-03.7-3.8 | **Complete** |
| 4 | Character Encoding | FR-04 | **Complete** |
| 5 | Pixel Data & Lazy Loading | FR-05 | **Complete** |
| 6 | Private Tags | FR-06 | **Complete** |
| 7 | File Writing | FR-07 | **Complete** |
| 8 | Validation & Strictness | FR-08 | **Complete** |
| 9 | RLE Codec | FR-09 | **Complete** |

---

## Milestone: v2.0.0 — Network, Codecs & De-identification

**Goal**: Full DICOM networking stack, pluggable image codecs, and standards-compliant de-identification

### Phase Overview

| Phase | Name | Requirements | Status |
|-------|------|--------------|--------|
| 10 | Network Foundation | FR-10.1, FR-10.2, FR-10.3, FR-10.4, FR-10.10, FR-10.11 | **Complete** |
| 11 | DIMSE Services | FR-10.5, FR-10.6, FR-10.7, FR-10.8, FR-10.9, FR-10.12 | **Complete** |
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
- [x] 11-01-PLAN.md — Common DIMSE types (QueryRetrieveLevel, progress structs, DicomCommand extensions)
- [x] 11-02-PLAN.md — CStoreScu service with streaming send and progress reporting
- [x] 11-03-PLAN.md — CFindScu service with IAsyncEnumerable and DicomQuery builder
- [x] 11-04-PLAN.md — C-STORE SCP handler support in DicomServer (buffered and streaming modes)
- [x] 11-05-PLAN.md — CMoveScu service for third-party retrieval
- [x] 11-06-PLAN.md — CGetScu service with inline C-STORE sub-operation handling
- [x] 11-07-PLAN.md — Integration tests (roundtrip and DCMTK interoperability)

**Must-haves**:
- [x] C-STORE SCU (send DICOM files to remote AE)
- [x] C-STORE SCP with streaming support (receive without full buffering)
- [x] C-FIND SCU (query PACS/RIS for studies/series/instances)
- [x] C-MOVE SCU (retrieve from PACS via sub-operations)
- [x] C-GET SCU (retrieve via C-STORE sub-ops on same association)
- [x] IAsyncEnumerable for C-FIND/C-MOVE/C-GET responses

**Should-haves**:
- [ ] Zero-copy PDU parsing via System.IO.Pipelines
- [x] Streaming C-STORE SCP handler with CopyToAsync pattern
- [ ] Element callback during network receive (validate/transform on arrival)
- [ ] Transfer syntax negotiation with transcoding capability

**Dependencies**: Phase 10 (association and PDU infrastructure)

**Research Needed**: Yes (C-MOVE third-party destination coordination, streaming receive patterns) - COMPLETE (11-RESEARCH.md)

**Success Criteria**:
- [x] Can send DICOM file to DCMTK storescp
- [x] Can receive DICOM file from DCMTK storescu
- [x] C-FIND returns matching studies from test PACS
- [x] C-MOVE triggers sub-operations to third-party destination
- [x] C-GET retrieves instances directly
- [x] Streaming receive does not buffer entire file in memory
- [ ] Association marked corrupted after PDU timeout (prevents data interleaving)

---

## Phase 12: Pure C# Codecs

**Goal**: JPEG and JPEG 2000 codecs implemented in pure C# for maximum portability and AOT compatibility

**Requirements**: FR-11.1, FR-11.2, FR-11.3, FR-11.4, FR-11.5, FR-11.6, FR-11.7

**Plans**: 7 plans in 4 waves

Plans:
- [ ] 12-01-PLAN.md — Infrastructure (TransferSyntax additions, JpegMarkers, HuffmanTable, QuantizationTable, ColorConversion)
- [ ] 12-02-PLAN.md — JPEG primitives (DctTransform, BitReader, BitWriter, JpegCodecOptions)
- [ ] 12-03-PLAN.md — JPEG Baseline codec (decoder, encoder, codec class, tests)
- [ ] 12-04-PLAN.md — JPEG Lossless codec (Predictor, LosslessHuffman, codec class, tests)
- [ ] 12-05-PLAN.md — JPEG 2000 infrastructure (J2kCodestream, DWT transforms, MqCoder)
- [ ] 12-06-PLAN.md — JPEG 2000 coding (EbcotEncoder/Decoder, PacketEncoder/Decoder, J2kEncoder/Decoder)
- [ ] 12-07-PLAN.md — Integration (Jpeg2000LosslessCodec, Jpeg2000LossyCodec, CodecInitializer, integration tests)

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

**Research Needed**: No - COMPLETE (12-CONTEXT.md, 12-RESEARCH.md)

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

**Research Needed**: No (P/Invoke patterns established, libjpeg-turbo/OpenJPEG well-documented)

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

**Dependencies**: Phase 4 (encoding for text element processing), Phase 7 (file writing for output)

**Research Needed**: Partial (two-pass UID remapping validation)

**Success Criteria**:
- [ ] Basic Profile removes all required tags per PS3.15 Annex E
- [ ] UID remapping maintains referential integrity across study
- [ ] Date shifting preserves temporal relationships within study
- [ ] De-identified files validate with DICOM validator
- [ ] Callback integration works with existing validation callbacks
- [ ] Warning raised for modalities with high burned-in PHI risk
- [ ] Roundtrip de-id -> re-id possible with mapping file

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

## v1.0.0 Phase Details (Archived)

### Phase 1: Core Data Model & Dictionary

**Goal**: Foundation data structures and source-generated dictionary

**Requirements**: FR-01.1-6, FR-02.1-5

**Plans**: 7 plans in 4 waves

Plans:
- [x] 01-01-PLAN.md — Project scaffolding + DicomTag, DicomVR primitives
- [x] 01-02-PLAN.md — NEMA XML caching + generator infrastructure
- [x] 01-03-PLAN.md — DicomUID, TransferSyntax, DicomDictionaryEntry
- [x] 01-04-PLAN.md — XML parsing and code emission
- [x] 01-05-PLAN.md — DicomElement hierarchy + DicomSequence
- [x] 01-06-PLAN.md — DicomDataset + integration
- [x] 01-07-PLAN.md — Generator tests + full verification

**Delivers**:
- DicomTag, DicomVR, DicomElement structs
- DicomDataset, DicomSequence classes
- DicomUID struct with inline storage
- Source generator parsing Part 6 XML
- Generated tag/UID/VR lookup tables

**Dependencies**: None (foundation)

**Research Needed**: No (well-documented patterns)

**Success Criteria**:
- [x] DicomTag equality/comparison/hashing works
- [x] Source generator produces ~4000 tag definitions
- [x] Dictionary lookup O(1) via FrozenDictionary
- [x] Unit tests pass on all TFMs

---

### Phase 2: Basic File Reading

**Goal**: Parse Explicit VR Little Endian files

**Requirements**: FR-03.1-3, FR-03.6

**Plans**: 4 plans in 2 waves

Plans:
- [x] 02-01-PLAN.md — DicomStreamReader for low-level Span-based parsing
- [x] 02-02-PLAN.md — Part10Reader for file structure parsing
- [x] 02-03-PLAN.md — DicomFileReader with async IAsyncEnumerable streaming
- [x] 02-04-PLAN.md — DicomFile class and integration tests

**Delivers**:
- DicomStreamReader ref struct for zero-copy parsing
- DicomReaderOptions with Strict/Lenient/Permissive presets
- Part 10 structure parsing (preamble, DICM, FMI)
- Explicit VR LE parsing
- DicomFile class for file I/O
- DicomFileReader with IAsyncEnumerable streaming

**Dependencies**: Phase 1 (data model)

**Research Needed**: No (Pipelines patterns established)

**Success Criteria**:
- [x] Part 10 structure parsing (preamble, DICM, FMI)
- [x] Explicit VR element reading
- [x] Transfer Syntax detection from File Meta Information
- [x] Tests pass with sample data (333 tests total)

---

### Phase 3: Implicit VR & Sequences

**Goal**: Handle real-world files with implicit VR and sequences

**Requirements**: FR-03.4, FR-03.7-8

**Plans**: 4 plans in 3 waves

Plans:
- [x] 03-01-PLAN.md — DicomReaderOptions extensions + implicit VR verification
- [x] 03-02-PLAN.md — SequenceParser for nested dataset parsing
- [x] 03-03-PLAN.md — DicomFileReader sequence integration
- [x] 03-04-PLAN.md — Context-dependent VR resolution + Phase 3 verification

**Delivers**:
- Implicit VR Little Endian support
- VR lookup from dictionary
- Defined length sequences
- Undefined length sequences with delimiters
- Nested sequence support

**Dependencies**: Phase 2 (file reading)

**Research Needed**: Partial (context-dependent VR edge cases)

**Success Criteria**:
- [x] Parse implicit VR test files
- [x] Nested sequences to depth 5+
- [x] Undefined length with delimiters
- [x] Context-dependent VR resolution

---

### Phase 4: Character Encoding

**Goal**: Correct text decoding for international data

**Requirements**: FR-04.1-5

**Plans**: 2 plans in 2 waves

Plans:
- [x] 04-01-PLAN.md — DicomEncoding core, character set registry, UTF-8 detection
- [x] 04-02-PLAN.md — DicomDataset encoding property, string element integration, inheritance

**Delivers**:
- DicomEncoding class with Primary/Extensions properties
- DicomCharacterSets registry (~30 standard DICOM character sets)
- UTF-8 zero-copy fast path via IsUtf8Compatible and TryGetUtf8
- Latin-1, ASCII, and ISO 2022 support
- Specific Character Set (0008,0005) parsing
- Sequence item encoding inheritance via Parent property
- DicomStringValue ref struct for zero-copy scenarios

**Dependencies**: Phase 3 (Parent property for inheritance)

**Research Needed**: Yes (ISO 2022 complexity) - COMPLETE

**Success Criteria**:
- [x] UTF-8 files decode correctly
- [x] Latin-1 files decode correctly
- [x] Multi-encoding datasets handled
- [x] Japanese/Chinese test files (stretch)

---

### Phase 5: Pixel Data & Lazy Loading

**Goal**: Handle large elements efficiently

**Requirements**: FR-05.1-5

**Plans**: 3 plans in 3 waves

Plans:
- [x] 05-01-PLAN.md — Core pixel data types (PixelDataInfo, PixelDataHandling, FragmentParser)
- [x] 05-02-PLAN.md — Lazy loading infrastructure (IPixelDataSource, DicomPixelDataElement)
- [x] 05-03-PLAN.md — Integration with DicomFileReader and DicomFile

**Delivers**:
- PixelDataInfo struct with frame size calculation
- PixelDataHandling enum (LoadInMemory, LazyLoad, Skip, Callback)
- DicomFragmentSequence with Basic Offset Table parsing
- FragmentParser for encapsulated pixel data
- IPixelDataSource interface with Immediate, Lazy, Skipped implementations
- DicomPixelDataElement with frame-level access
- DicomReaderOptions.PixelDataHandling configuration
- DicomFile.PixelData property for convenient access

**Dependencies**: Phase 3 (fragment sequences like SQ), Phase 4 (encoding for metadata)

**Research Needed**: Partial (fragment edge cases) - COMPLETE

**Success Criteria**:
- [x] Load uncompressed pixel data
- [x] Parse encapsulated fragments
- [x] Lazy loading skips pixel data
- [x] Multi-frame datasets work

---

### Phase 6: Private Tags

**Goal**: Preserve vendor-specific data with proper creator tracking

**Requirements**: FR-06.1-4

**Plans**: 2 plans in 2 waves

Plans:
- [x] 06-01-PLAN.md — Vendor dictionary source generator (parse malaterre XMLs, generate code)
- [x] 06-02-PLAN.md — PrivateCreatorDictionary enhancements (slot allocation, compaction, lookup)

**Delivers**:
- VendorDictionary source-generated from malaterre/dicom-private-dicts
- PrivateTagInfo and PrivateCreatorInfo record structs
- Enhanced PrivateCreatorDictionary with AllocateSlot, Compact methods
- DicomDatasetExtensions for StripPrivateTags and AddPrivateElement
- DicomReaderOptions extensions for private tag handling
- Siemens, GE, Philips vendor dictionaries bundled

**Dependencies**: Phase 3 (private tags in sequences)

**Research Needed**: No (patterns clear) - COMPLETE (06-RESEARCH.md)

**Success Criteria**:
- [x] Private creator tracking
- [x] Siemens/GE/Philips tags recognized
- [x] Strip-private callback works
- [x] Roundtrip preserves private data

---

### Phase 7: File Writing

**Goal**: Write valid DICOM Part 10 files

**Requirements**: FR-07.1-5

**Plans**: 3 plans in 3 waves

Plans:
- [x] 07-01-PLAN.md — DicomStreamWriter for low-level IBufferWriter-based element writing
- [x] 07-02-PLAN.md — DicomFileWriter and File Meta Information generation
- [x] 07-03-PLAN.md — Sequence length handling and roundtrip tests

**Delivers**:
- DicomStreamWriter for IBufferWriter<byte> element serialization
- DicomFileWriter for Part 10 file output
- FileMetaInfoGenerator with group length calculation
- DicomWriterOptions (transfer syntax, length encoding, FMI settings)
- SequenceLengthCalculator for defined-length mode
- Undefined length (delimiter) and defined length sequence writing
- Full roundtrip read-write-read verification

**Dependencies**: Phase 4 (encoding for string writing), Phase 3 (sequences)

**Research Needed**: No (mirror of reading) - COMPLETE

**Success Criteria**:
- [x] Written files validate with dcmtk
- [x] Roundtrip read->write->read identical
- [x] Streaming write to network
- [x] Both length modes work

---

### Phase 8: Validation & Strictness

**Goal**: Configurable parsing behavior with comprehensive validation options

**Requirements**: FR-08.1-4

**Plans**: 3 plans in 3 waves

Plans:
- [x] 08-01-PLAN.md — ValidationIssue, ValidationResult, IValidationRule
- [x] 08-02-PLAN.md — Built-in VR validators (date, time, UID, character constraints)
- [x] 08-03-PLAN.md — ValidationProfile presets and integration

**Delivers**:
- ValidationIssue record with full context (tag, VR, position, message, code)
- ValidationResult collection with severity filtering
- IValidationRule interface for pluggable validation
- ValidationCodes constants (DICOM-001 through DICOM-025)
- ElementValidationContext for validation rule input
- 9 built-in VR validators (DA, TM, DT, AS, UI, PN, CS, length, characters)
- ValidationProfile presets (Strict, Lenient, Permissive, None)
- DicomReaderOptions validation integration (ValidationProfile, ValidationCallback)
- StandardRules.All and StandardRules.StructuralOnly collections

**Dependencies**: Phase 4 (encoding for character validation)

**Research Needed**: Yes - COMPLETE (08-RESEARCH.md)

**Success Criteria**:
- [x] Strict mode rejects bad files
- [x] Lenient mode recovers gracefully
- [x] Validation callback invoked
- [x] Issues reported with context

---

### Phase 9: RLE Codec

**Goal**: Validate codec interface with built-in codec

**Requirements**: FR-09.1-3

**Plans**: 2 plans in 2 waves

Plans:
- [x] 09-01-PLAN.md — IPixelDataCodec interface, CodecRegistry, CodecCapabilities
- [x] 09-02-PLAN.md — RLE decoder/encoder implementation with SIMD optimization

**Delivers**:
- IPixelDataCodec interface
- CodecRegistry with FrozenDictionary lookup
- CodecCapabilities, DecodeResult, PixelDataInfo types
- DicomCodecException for codec errors
- RleCodec with SIMD-optimized encoding
- RleSegmentHeader for 64-byte header parsing
- PackBits encode/decode (TIFF 6.0 algorithm)

**Dependencies**: Phase 5 (pixel data infrastructure)

**Research Needed**: No (RLE spec straightforward) - COMPLETE

**Success Criteria**:
- [x] Decode RLE-compressed files
- [x] Encode to RLE format
- [x] Codec interface extensible
- [x] No external dependencies

---

*Last updated: 2026-01-29 (Phase 11 complete)*
