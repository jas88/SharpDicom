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

## Phase 1: Core Data Model & Dictionary

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

## Phase 2: Basic File Reading

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

## Phase 3: Implicit VR & Sequences

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

## Phase 4: Character Encoding

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

## Phase 5: Pixel Data & Lazy Loading

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

## Phase 6: Private Tags

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

## Phase 7: File Writing

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

## Phase 8: Validation & Strictness

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

## Phase 9: RLE Codec

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

## Critical Path

```
Phase 1 (Data Model)
    |
Phase 2 (File Reading)
    |
Phase 3 (Implicit VR & Sequences)
    |
    +-- Phase 4 (Encoding) ----------+
    |                                |
    +-- Phase 5 (Pixel Data) --------+
    |       |                        |
    |   Phase 9 (RLE) ---------------+
    |                                |
    +-- Phase 6 (Private Tags) -- Phase 7 (Writing)
                                     |
                              Phase 8 (Validation)
```

**Parallelization opportunities**:
- Phase 4 + Phase 5 can run in parallel after Phase 3
- Phase 6 + Phase 9 can run in parallel
- Phase 8 depends on Phase 4 (encoding for character validation)

---

*Last updated: 2026-01-27 (v1.0.0 milestone complete)*
