# SharpDicom Architecture

SharpDicom is a complete DICOM toolkit for .NET built from scratch with no external DICOM library dependencies.

## Design Goals

- **Pure .NET implementation**: No fo-dicom or other DICOM library dependencies
- **Modern .NET patterns**: Span<T>-first, streaming, async I/O
- **High performance**: Zero-allocation paths where possible
- **DICOM standards compliance**: Full conformance to DICOM PS3 specification

## Architecture Overview

```
SharpDicom/
├── Data/           # DICOM data model (Dataset, Tags, VRs, UIDs)
├── IO/             # File reading/writing (Part 10 format)
├── Codecs/         # Pixel data compression/decompression
├── Validation/     # Element validation rules and profiles
├── Internal/       # Polyfills, helpers
└── Network/        # (Planned) DIMSE services, association, PDUs
```

## Documentation

| Document | Description |
|----------|-------------|
| [Data Model](data-model.md) | DicomTag, DicomVR, DicomElement, DicomDataset, DicomUID, exceptions |
| [File I/O](io.md) | Part 10 file format, reading, writing, streaming, codecs |
| [Network](network.md) | Part 8 networking, PDUs, DIMSE services (planned) |
| [Character Encoding](encoding.md) | Character sets, DicomEncoding, zero-copy string access |
| [Design Principles](design-principles.md) | Span<T>-first, streaming, async, minimal allocations |

## DICOM Standards Reference

Implementation follows DICOM standard (PS3):

| Part | Content | Relevance |
|------|---------|-----------|
| PS3.3 | Information Object Definitions | Data model |
| PS3.5 | Data Structures and Encoding | Parsing/writing |
| PS3.6 | Data Dictionary | Tags, VRs, UIDs |
| PS3.7 | Message Exchange | DIMSE services |
| PS3.8 | Network Communication | Association, PDUs |
| PS3.10 | Media Storage | File format |

Reference: [DICOM Standard](https://www.dicomstandard.org/current)
