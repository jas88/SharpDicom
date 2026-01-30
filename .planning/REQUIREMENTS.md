# SharpDicom Requirements

## Version: 2.0.0

### Functional Requirements

#### FR-10: DICOM Networking (Part 8)
| ID | Requirement | Priority | Source |
|----|-------------|----------|--------|
| FR-10.1 | PDU parsing and building (A-ASSOCIATE, P-DATA, A-RELEASE, A-ABORT) | Must | Research |
| FR-10.2 | Association negotiation with presentation contexts | Must | Research |
| FR-10.3 | C-ECHO SCU (verify connectivity) | Must | DICOM Standard |
| FR-10.4 | C-ECHO SCP (respond to verification) | Must | DICOM Standard |
| FR-10.5 | C-STORE SCU (send DICOM files) | Must | DICOM Standard |
| FR-10.6 | C-STORE SCP with streaming support | Must | DICOM Standard |
| FR-10.7 | C-FIND SCU (query remote PACS) | Must | DICOM Standard |
| FR-10.8 | C-MOVE SCU (retrieve from PACS) | Must | DICOM Standard |
| FR-10.9 | C-GET SCU (retrieve via C-STORE sub-ops) | Must | DICOM Standard |
| FR-10.10 | DicomClient class with async API | Must | CLAUDE.md |
| FR-10.11 | DicomServer class with event-based handlers | Must | CLAUDE.md |
| FR-10.12 | Zero-copy PDU parsing via System.IO.Pipelines | Should | Research |

#### FR-11: Image Codecs (Pure C#)
| ID | Requirement | Priority | Source |
|----|-------------|----------|--------|
| FR-11.1 | JPEG Baseline codec (8-bit lossy, Process 1) | Must | User |
| FR-11.2 | JPEG Lossless codec (Process 14, Selection Value 1) | Must | User |
| FR-11.3 | JPEG 2000 Lossless codec | Must | User |
| FR-11.4 | JPEG 2000 Lossy codec | Should | User |
| FR-11.5 | Pure C# implementations (no native dependencies) | Must | User |
| FR-11.6 | Trim/AOT compatible | Must | PROJECT.md |
| FR-11.7 | Register via existing IPixelDataCodec interface | Must | Architecture |

#### FR-12: Native Codecs Package (Optional)
| ID | Requirement | Priority | Source |
|----|-------------|----------|--------|
| FR-12.1 | SharpDicom.Codecs NuGet package | Should | User |
| FR-12.2 | Native JPEG codec (libjpeg-turbo) | Should | Research |
| FR-12.3 | Native JPEG 2000 codec (OpenJPEG) | Should | Research |
| FR-12.4 | Override registration for pure C# codecs | Should | Architecture |
| FR-12.5 | Cross-platform natives (win-x64, linux-x64, osx-arm64) | Should | Research |

#### FR-13: De-identification
| ID | Requirement | Priority | Source |
|----|-------------|----------|--------|
| FR-13.1 | PS3.15 Basic Application Level Confidentiality Profile | Must | User |
| FR-13.2 | Source-generated action table from NEMA part15.xml | Must | Research |
| FR-13.3 | UID remapping with consistent study-level replacement | Must | User |
| FR-13.4 | Date shifting with configurable offset | Must | User |
| FR-13.5 | Integration with existing element callback system | Must | Architecture |
| FR-13.6 | DicomDeidentifier class with fluent configuration | Should | Research |

---

### Non-Functional Requirements

#### NFR-04: Networking Performance
| ID | Requirement | Target | Source |
|----|-------------|--------|--------|
| NFR-04.1 | Zero-copy PDU parsing where possible | Design | Research |
| NFR-04.2 | Streaming C-STORE without full file buffering | Design | Research |
| NFR-04.3 | Configurable PDU size (16KB-1MB) | Design | Research |

#### NFR-05: Codec Performance
| ID | Requirement | Target | Source |
|----|-------------|--------|--------|
| NFR-05.1 | Pure C# codecs: acceptable for typical use | Design | User |
| NFR-05.2 | Native codecs: 10-50× faster than pure C# | Benchmark | Research |
| NFR-05.3 | No memory leaks in codec operations | Design | Research |

### Out of Scope (v3+)

| Feature | Reason |
|---------|--------|
| TLS support | Orthogonal to core networking; v2 focuses on unencrypted connections |
| Modality Worklist (MWL) | Niche RIS integration |
| DIMSE-N services | Normalized objects are <5% of use cases |
| C-FIND SCP / C-MOVE SCP | Most users are SCU; SCP is complex |
| JPEG-LS codec | Less common than JPEG/J2K |
| HTJ2K codec | Emerging standard, defer |
| Burned-in PHI detection | Requires OCR, too complex for v2 |
| Multiple de-id profiles | Basic profile covers 90% of use cases |

### Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| FR-10.1 | Phase 10 | Pending |
| FR-10.2 | Phase 10 | Pending |
| FR-10.3 | Phase 10 | Pending |
| FR-10.4 | Phase 10 | Pending |
| FR-10.5 | Phase 11 | Pending |
| FR-10.6 | Phase 11 | Pending |
| FR-10.7 | Phase 11 | Pending |
| FR-10.8 | Phase 11 | Pending |
| FR-10.9 | Phase 11 | Pending |
| FR-10.10 | Phase 10 | Pending |
| FR-10.11 | Phase 10 | Pending |
| FR-10.12 | Phase 11 | Pending |
| FR-11.1 | Phase 12 | Complete |
| FR-11.2 | Phase 12 | Complete |
| FR-11.3 | Phase 12 | Complete |
| FR-11.4 | Phase 12 | Complete |
| FR-11.5 | Phase 12 | Complete |
| FR-11.6 | Phase 12 | Complete |
| FR-11.7 | Phase 12 | Complete |
| FR-12.1 | Phase 13 | Complete |
| FR-12.2 | Phase 13 | Complete |
| FR-12.3 | Phase 13 | Complete |
| FR-12.4 | Phase 13 | Complete |
| FR-12.5 | Phase 13 | Complete |
| FR-13.1 | Phase 14 | Pending |
| FR-13.2 | Phase 14 | Pending |
| FR-13.3 | Phase 14 | Pending |
| FR-13.4 | Phase 14 | Pending |
| FR-13.5 | Phase 14 | Pending |
| FR-13.6 | Phase 14 | Pending |

**Coverage:**
- v2 requirements: 30 total
- Mapped to phases: 30
- Unmapped: 0 ✓

---
*Requirements defined: 2026-01-27*
*Last updated: 2026-01-27 after v2.0.0 scope finalization*
