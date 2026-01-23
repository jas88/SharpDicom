# Project Research Summary

**Project:** SharpDicom
**Domain:** DICOM Medical Imaging Library
**Researched:** 2026-01-26
**Confidence:** HIGH

## Executive Summary

SharpDicom is a high-performance DICOM toolkit built from scratch for .NET, requiring mastery of both DICOM protocol intricacies and modern .NET performance patterns. Research confirms that the streaming-first, Span<T>-based architecture outlined in CLAUDE.md aligns with how experts build high-performance binary parsers (similar to System.Text.Json's Utf8JsonReader pattern). The two-layer architecture (ref struct for parsing, class wrapper for async I/O) combined with System.IO.Pipelines provides the optimal balance of performance and usability.

The recommended approach prioritizes: (1) core data model and dictionary generation, (2) streaming file I/O with configurable strictness modes, (3) pixel data handling with lazy loading, (4) network protocol support. Research shows that real-world DICOM files frequently violate the standard in predictable ways, making lenient-by-default parsing essential while preserving strict mode for conformance testing.

Key risks center on three areas: parsing non-conformant files (missing preambles, invalid VRs, wrong transfer syntax), handling context-dependent VRs in implicit VR files, and character encoding complexity (ISO 2022 escape sequences, GB18030 backslash ambiguity). Mitigation requires comprehensive test data from multiple vendors, callback-based validation architecture, and explicit handling modes (strict/lenient/permissive) throughout.

## Key Findings

### Recommended Stack

The stack research validates the design decisions in CLAUDE.md with specific implementation patterns.

**Core technologies:**
- **Roslyn Incremental Source Generator**: DICOM dictionary generation from NEMA XML with FrozenDictionary on .NET 8+ fallback to Dictionary on older TFMs
- **System.IO.Pipelines**: Buffer management and backpressure for streaming file/network I/O
- **Span<T>/Memory<T>**: Zero-allocation parsing with clear ownership semantics (borrowed vs owned buffers)
- **ArrayPool<byte>/MemoryPool<byte>**: Buffer pooling with explicit return patterns
- **FrozenDictionary (.NET 8+)**: Optimized lookups for tag dictionary (~4000 entries), UID registry, transfer syntaxes

**Multi-TFM strategy:**
- netstandard2.0: System.Memory package polyfills
- net6.0/net8.0/net9.0: Progressive feature enablement
- Source generator: conditional compilation for FrozenDictionary vs Dictionary

### Expected Features

**Must have (table stakes):**
- Read/write DICOM Part 10 files (Implicit VR LE, Explicit VR LE)
- Complete data dictionary from NEMA standard (tags, VRs, UIDs)
- Sequence handling with defined and undefined length
- Character set support (ASCII, UTF-8, Latin-1)
- Private tag preservation with creator tracking
- Native pixel data handling (uncompressed)
- RLE codec (no external dependencies)

**Should have (competitive):**
- Encapsulated pixel data with fragment sequence support
- Basic Offset Table parsing and generation
- Streaming/lazy loading for large elements
- Major vendor private dictionaries (Siemens, GE, Philips)
- ISO 2022 character encoding (Japanese, Korean, Chinese)
- Configurable strictness (strict/lenient/permissive presets)
- Callback-based element processing and validation

**Defer (v2+):**
- JPEG/JPEG2000/JPEG-LS codecs (separate NuGet packages)
- Network protocol (DIMSE, C-ECHO, C-STORE, C-FIND)
- Video transfer syntaxes (MPEG, HEVC)
- De-identification engine (PS3.15 profiles)
- Extended Offset Table (64-bit, for >4GB files)

### Architecture Approach

The architecture follows a two-layer streaming design inspired by System.Text.Json: a ref struct `DicomStreamReader` for zero-allocation parsing on spans (cannot cross await boundaries), wrapped by a class `DicomFileReader` that manages async I/O via System.IO.Pipelines and exposes `IAsyncEnumerable<DicomElement>`. State machine parsing with externalized state enables resumable parsing across buffer boundaries. Lazy loading via callbacks handles large elements without memory pressure.

**Major components:**
1. **Data Layer** (DicomTag, DicomVR, DicomElement, DicomDataset, DicomSequence) - Core data model with compact struct representations
2. **Dictionary Layer** (Source-generated from NEMA XML) - Static tag/UID/VR lookup with FrozenDictionary on .NET 8+
3. **I/O Layer** (DicomStreamReader ref struct, DicomFileReader class) - Two-layer streaming with state machine parsing
4. **Transfer Syntax Layer** - Encoding definitions with pluggable codec interface
5. **Encoding Layer** (DicomEncoding, character set registry) - UTF-8 fast path, ISO 2022 state machine

### Critical Pitfalls

1. **Missing preamble/DICM prefix** - Many older files lack Part 10 wrapper; implement heuristic detection and FilePreambleHandling.Optional mode
2. **Invalid VR codes (0x2020 spaces, null bytes)** - Map to UN in lenient mode, preserve original in permissive; never crash
3. **Context-dependent VRs in implicit files** - Deferred VR resolution storing raw bytes until context tags (BitsAllocated, PixelRepresentation) are seen
4. **ISO 2022 escape sequences and GB18030 backslash** - 0x5C can appear in multi-byte sequences; must not split on backslash without encoding awareness
5. **UN with undefined length** - Must parse as implicit VR sequence per DICOM standard; only SQ can have undefined length

## Implications for Roadmap

Based on research, suggested phase structure:

### Phase 1: Core Data Model & Dictionary
**Rationale:** Foundation for all other components; source generator must work first
**Delivers:** DicomTag, DicomVR, DicomElement structs; source-generated dictionary from part06.xml
**Addresses:** Data model foundation (table stakes)
**Avoids:** Dictionary initialization failure (critical pitfall for implicit VR)

### Phase 2: Basic File Reading
**Rationale:** Must parse files before implementing advanced features; establishes streaming architecture
**Delivers:** DicomFileReader with state machine, Part 10 structure parsing, Explicit VR LE support
**Uses:** System.IO.Pipelines, Span<T> patterns, FrozenDictionary
**Implements:** I/O Layer, two-layer reader architecture

### Phase 3: Implicit VR & Sequences
**Rationale:** Required for real-world file compatibility; builds on Phase 2 parser
**Delivers:** Implicit VR Little Endian, sequence parsing with nesting, undefined length handling
**Addresses:** Sequence handling (table stakes), context-dependent VR resolution
**Avoids:** UN undefined length pitfall, nested sequence delimiter confusion

### Phase 4: Character Encoding
**Rationale:** Required for correct metadata; can be added after basic parsing works
**Delivers:** DicomEncoding class, UTF-8 fast path, Latin-1, ASCII, ISO 2022 state machine
**Addresses:** Character set support (table stakes)
**Avoids:** ISO 2022 escape sequence issues, GB18030 backslash ambiguity

### Phase 5: Pixel Data & Lazy Loading
**Rationale:** Complex element handling; requires stable sequence parsing from Phase 3
**Delivers:** Native pixel data, encapsulated fragments, BOT parsing, lazy loading callbacks
**Addresses:** Pixel data handling (table stakes), streaming support (competitive)
**Avoids:** Memory exhaustion on large studies, odd-length fragment issues

### Phase 6: Private Tags & Vendor Dictionaries
**Rationale:** Refinement after core parsing stable; low priority for v1.0
**Delivers:** PrivateCreatorDictionary, bundled vendor dictionaries, configurable handling
**Addresses:** Private tag preservation (table stakes)
**Avoids:** Missing private creator issues, VR confusion in implicit files

### Phase 7: File Writing
**Rationale:** Read before write; can reuse data model from earlier phases
**Delivers:** DicomFileWriter, IBufferWriter<byte> support, defined/undefined length sequences
**Addresses:** File writing (table stakes)

### Phase 8: Validation & Strictness Modes
**Rationale:** Polish after core features; callback system benefits from stable element model
**Delivers:** Strict/lenient/permissive presets, callback-based validation, ParsingIssue reporting
**Addresses:** Configurable strictness (competitive)

### Phase 9: RLE Codec
**Rationale:** Only codec without external dependencies; validates codec interface design
**Delivers:** RLE compression/decompression, IPixelDataCodec interface
**Addresses:** RLE codec (table stakes)

### Phase Ordering Rationale

- **Phases 1-3 are dependencies:** Cannot parse files without dictionary, cannot handle sequences without VR resolution
- **Phase 4 (encoding) after basic parsing:** Character encoding is orthogonal; can add to working parser
- **Phase 5 (pixel data) requires sequences:** Encapsulated pixel data uses sequence-like structure
- **Phases 6-8 are refinements:** Add value but not blocking for core functionality
- **Phase 9 validates codec API:** Design codec interface, prove it works before adding external codecs

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 4 (Character Encoding):** ISO 2022 escape sequences complex; need ICU-like test cases
- **Phase 5 (Pixel Data):** Fragment/frame relationship edge cases; need multi-vendor test files

Phases with standard patterns (skip research-phase):
- **Phase 1 (Data Model):** Well-documented in CLAUDE.md, standard struct patterns
- **Phase 2 (File Reading):** System.IO.Pipelines patterns well-established
- **Phase 7 (File Writing):** Mirror of reading with IBufferWriter

## Critical Design Decisions

| Decision | Options | Recommendation | Rationale |
|----------|---------|----------------|-----------|
| Dictionary storage | Runtime parsing vs Source generation | Source generation | Compile-time safety, no I/O at startup, FrozenDictionary on .NET 8+ |
| Reader architecture | Single class vs Two-layer (ref struct + class) | Two-layer | Zero-allocation hot path, async I/O support, follows Utf8JsonReader pattern |
| Buffer ownership | Implicit (copy always) vs Explicit (ToOwned()) | Explicit ToOwned() | Streaming efficiency, clear ownership semantics, matches Pipelines model |
| Default strictness | Strict vs Lenient | Lenient | Real-world files violate standard; strict mode available for validation |
| Pixel data handling | Load all vs Lazy | Configurable with lazy default | Memory efficiency for large studies; callback for per-instance decisions |
| VR storage | Enum vs 2-byte struct | 2-byte struct | Compact, direct byte comparison, handles invalid VRs gracefully |
| Tag storage | Two ushorts vs Single uint | Single uint | 4-byte struct, trivial equality/hashing, group/element as properties |

## Technical Risks

| Risk | Severity | Likelihood | Mitigation |
|------|----------|------------|------------|
| ISO 2022 encoding complexity | High | Medium | Incremental implementation, extensive test data from Asian locales |
| Context-dependent VR resolution | High | High | Deferred parsing with raw byte storage, context accumulation |
| Non-conformant file rejection | Medium | High | Default lenient mode, comprehensive heuristics, callback reporting |
| Performance regression vs fo-dicom | Medium | Low | Benchmark suite from day one, Pipelines architecture proven |
| Memory pressure on large studies | High | Medium | Lazy loading default, memory-mapped files for >2GB, streaming API |

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | Microsoft documentation, proven patterns (System.Text.Json, Kestrel) |
| Features | HIGH | DICOM standard explicit, fo-dicom as feature reference |
| Architecture | HIGH | Follows established patterns, detailed design in CLAUDE.md |
| Pitfalls | HIGH | fo-dicom issues, pydicom issues, DCMTK forums, real-world experience |

**Overall confidence:** HIGH

### Gaps to Address

- **Vendor-specific CSA header parsing:** Siemens binary format details; defer to Phase 6
- **Extended Offset Table (64-bit):** Rare in practice; defer to v2
- **Video transfer syntax frame handling:** Different model (stream vs frames); defer to v2
- **Network protocol edge cases:** Research during network phase planning

## Sources

### Primary (HIGH confidence)
- DICOM Standard PS3.5, PS3.6, PS3.8, PS3.10 - Data structures, dictionary, encoding
- Microsoft Documentation - System.IO.Pipelines, Span<T>, FrozenDictionary, Source Generators
- Roslyn Incremental Generators documentation and cookbook

### Secondary (MEDIUM confidence)
- fo-dicom GitHub issues (#1847, #64, #1146, #1789) - Real-world parsing pitfalls
- pydicom GitHub issues (#1140, #1942) - Additional edge cases
- DCMTK forums - Implicit VR handling, private tags

### Tertiary (LOW confidence)
- Vendor-specific documentation (Siemens CSA, GE, Philips) - May be incomplete

---
*Research completed: 2026-01-26*
*Ready for roadmap: yes*
