# SharpDicom Requirements

## Version: 1.0.0 (MVP)

### Functional Requirements

#### FR-01: Core Data Model
| ID | Requirement | Priority | Source |
|----|-------------|----------|--------|
| FR-01.1 | DicomTag as 4-byte struct with group/element properties | Must | CLAUDE.md |
| FR-01.2 | DicomVR as 2-byte struct with validation | Must | CLAUDE.md |
| FR-01.3 | DicomElement struct with typed accessors | Must | CLAUDE.md |
| FR-01.4 | DicomDataset with O(1) lookup, sorted enumeration | Must | CLAUDE.md |
| FR-01.5 | DicomSequence class with nested dataset support | Must | Research |
| FR-01.6 | DicomUID struct with inline 64-byte storage | Must | CLAUDE.md |

#### FR-02: DICOM Dictionary
| ID | Requirement | Priority | Source |
|----|-------------|----------|--------|
| FR-02.1 | Source generator consuming NEMA Part 6 XML | Must | PROJECT.md |
| FR-02.2 | Static DicomTag members for all ~4000 standard tags | Must | Research |
| FR-02.3 | Static DicomUID members for transfer syntaxes, SOP classes | Must | Research |
| FR-02.4 | FrozenDictionary on .NET 8+, Dictionary fallback | Should | Research |
| FR-02.5 | VR info lookup (name, padding, max length) | Must | CLAUDE.md |

#### FR-03: File Reading
| ID | Requirement | Priority | Source |
|----|-------------|----------|--------|
| FR-03.1 | Parse DICOM Part 10 files with preamble/DICM | Must | PROJECT.md |
| FR-03.2 | Parse files without preamble (heuristic detection) | Must | Pitfalls |
| FR-03.3 | Explicit VR Little Endian support | Must | Research |
| FR-03.4 | Implicit VR Little Endian support | Must | Research |
| FR-03.5 | Streaming element-by-element iteration | Must | PROJECT.md |
| FR-03.6 | File Meta Information (Group 0002) parsing | Must | Research |
| FR-03.7 | Defined length sequence/item parsing | Must | Research |
| FR-03.8 | Undefined length sequence/item parsing | Must | Research |

#### FR-04: Character Encoding ✅
| ID | Requirement | Priority | Status |
|----|-------------|----------|--------|
| FR-04.1 | ASCII/ISO-IR 6 default encoding | Must | ✅ Complete |
| FR-04.2 | UTF-8 (ISO-IR 192) with zero-copy fast path | Must | ✅ Complete |
| FR-04.3 | Latin-1 (ISO-IR 100) support | Must | ✅ Complete |
| FR-04.4 | Specific Character Set (0008,0005) parsing | Must | ✅ Complete |
| FR-04.5 | ISO 2022 escape sequences (JIS, GB18030) | Should | ✅ Complete |

#### FR-05: Pixel Data
| ID | Requirement | Priority | Source |
|----|-------------|----------|--------|
| FR-05.1 | Native (uncompressed) pixel data reading | Must | Research |
| FR-05.2 | Encapsulated pixel data with fragment sequence | Must | Research |
| FR-05.3 | Basic Offset Table parsing | Must | Research |
| FR-05.4 | Configurable handling (load/lazy/skip/callback) | Must | CLAUDE.md |
| FR-05.5 | Multi-frame pixel data support | Must | Research |

#### FR-06: Private Tags
| ID | Requirement | Priority | Source |
|----|-------------|----------|--------|
| FR-06.1 | Private creator element tracking | Must | CLAUDE.md |
| FR-06.2 | Private tag slot resolution | Must | Research |
| FR-06.3 | Configurable retention (keep/strip) | Should | CLAUDE.md |
| FR-06.4 | Bundled vendor dictionaries (Siemens, GE, Philips) | Could | Research |

#### FR-07: File Writing
| ID | Requirement | Priority | Source |
|----|-------------|----------|--------|
| FR-07.1 | Write DICOM Part 10 files with preamble/DICM | Must | PROJECT.md |
| FR-07.2 | File Meta Information generation | Must | Research |
| FR-07.3 | Explicit VR Little Endian output | Must | Research |
| FR-07.4 | Streaming write via IBufferWriter<byte> | Should | Research |
| FR-07.5 | Defined/undefined length sequence writing | Should | Research |

#### FR-08: Validation
| ID | Requirement | Priority | Source |
|----|-------------|----------|--------|
| FR-08.1 | Strict/lenient/permissive mode presets | Must | Research |
| FR-08.2 | Callback-based element validation | Should | CLAUDE.md |
| FR-08.3 | VR mismatch detection | Should | Research |
| FR-08.4 | Value length validation | Should | Research |

#### FR-09: RLE Codec
| ID | Requirement | Priority | Source |
|----|-------------|----------|--------|
| FR-09.1 | RLE decompression without external dependencies | Should | Research |
| FR-09.2 | RLE compression | Could | Research |
| FR-09.3 | IPixelDataCodec interface for future codecs | Must | CLAUDE.md |

### Non-Functional Requirements

#### NFR-01: Performance
| ID | Requirement | Target | Source |
|----|-------------|--------|--------|
| NFR-01.1 | File parsing 2-3× faster than fo-dicom | Benchmark | PROJECT.md |
| NFR-01.2 | 50%+ lower memory allocations | Benchmark | PROJECT.md |
| NFR-01.3 | Zero-allocation hot paths where possible | Design | CLAUDE.md |
| NFR-01.4 | Sub-millisecond element access | Design | CLAUDE.md |

#### NFR-02: Compatibility
| ID | Requirement | Target | Source |
|----|-------------|--------|--------|
| NFR-02.1 | netstandard2.0 support | Must | PROJECT.md |
| NFR-02.2 | net8.0 LTS support | Must | PROJECT.md |
| NFR-02.3 | net9.0 latest support | Must | PROJECT.md |
| NFR-02.4 | Trimming compatible (no reflection) | Must | PROJECT.md |
| NFR-02.5 | Native AOT compatible | Must | PROJECT.md |

#### NFR-03: Quality
| ID | Requirement | Target | Source |
|----|-------------|--------|--------|
| NFR-03.1 | Unit test coverage > 80% | CI | Standards |
| NFR-03.2 | Roundtrip tests (read → write → read) | CI | Research |
| NFR-03.3 | Multi-vendor test file coverage | CI | Research |
| NFR-03.4 | Warnings as errors | Build | Standards |

### Out of Scope (v2+)

| Feature | Reason |
|---------|--------|
| DICOM networking (DIMSE) | Complexity; v2 milestone |
| JPEG/JPEG2000/JPEG-LS codecs | External dependencies; separate packages |
| De-identification engine | Requires stable core; v2 |
| Video transfer syntaxes | Different model; v2 |
| fo-dicom API compatibility layer | Migration tooling; parallel track |
| MongoDB/BSON serialization | v2 feature |

### Acceptance Criteria

**v1.0 is complete when:**
1. ✅ Parse standard Explicit VR LE files
2. ✅ Parse Implicit VR LE files
3. ✅ Parse files without preamble
4. ✅ Handle sequences (defined + undefined length)
5. ✅ Read/write roundtrip preserves data
6. ✅ Character encoding works for UTF-8 and Latin-1
7. ✅ Pixel data accessible (load or lazy)
8. ✅ Private tags preserved with creator tracking
9. ✅ Benchmarks show 2× fo-dicom performance
10. ✅ Multi-target build passes on all TFMs

---
*Derived from: PROJECT.md, CLAUDE.md, research synthesis*
*Last updated: 2026-01-27 (FR-04 complete)*
