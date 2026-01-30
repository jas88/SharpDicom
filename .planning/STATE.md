# SharpDicom Project State

## Current Status

**Milestone**: v2.0.0 - Network, Codecs & De-identification SHIPPED
**Phase**: Between milestones
**Status**: Ready to plan next milestone
**Last activity**: 2026-01-30 - v2.0.0 milestone archived

## Project Reference

See: .planning/PROJECT.md (updated 2026-01-30)

**Core value:** Efficient streaming of DICOM data between network, disk, and document databases without materializing entire datasets in memory.

**Current focus:** Planning next milestone

## Shipped Milestones

| Milestone | Name | Phases | Plans | Shipped |
|-----------|------|--------|-------|---------|
| v1.0.0 | Core DICOM Toolkit | 1-9 | 30 | 2026-01-27 |
| v2.0.0 | Network, Codecs & De-identification | 10-14 | 38 | 2026-01-30 |

**Total:** 14 phases, 68 plans executed

## Test Status

- 3870 tests passing
- 0 failed
- 126 skipped (external service tests for Orthanc/DCMTK)

## Key Accomplishments

### v1.0.0
- Core data model with source-generated DICOM dictionary (4000+ tags, 1000+ UIDs)
- Streaming file reading with Span<T>-first architecture
- Implicit VR and sequence parsing with depth guards
- Character encoding (UTF-8, ISO 8859-x, CJK, ISO 2022)
- Pixel data with lazy loading and fragment support
- Private tag support with vendor dictionaries (9268 tags)
- File writing with sequence support (both length modes)
- Validation framework with Strict/Lenient/Permissive profiles
- RLE codec with SIMD optimization

### v2.0.0
- Full DICOM networking stack — PDU parsing, association, C-ECHO/C-STORE/C-FIND/C-MOVE/C-GET SCU/SCP
- Pure C# image codecs — JPEG Baseline, JPEG Lossless, JPEG 2000 Lossless/Lossy
- Native codec acceleration — libjpeg-turbo, OpenJPEG, CharLS, FFmpeg via Zig
- GPU support — nvJPEG2000 integration with CPU fallback
- PS3.15 de-identification — Source-generated action tables, UID remapping, date shifting, pixel redaction
- Batch processing — Parallel directory de-identification with progress reporting

## What's Next

Potential v3.0 features:
- DICOM-Web APIs (WADO-RS, STOW-RS, QIDO-RS)
- MongoDB/BSON serialization
- CLI tools (dcmdump, storescu, findscu equivalents)
- TLS support for networking
- Transfer syntax negotiation with transcoding

## Archives

- `.planning/milestones/v2.0.0-ROADMAP.md` — Full v2.0.0 phase details
- `.planning/milestones/v2.0.0-REQUIREMENTS.md` — v2.0.0 requirements with outcomes
- `.planning/MILESTONES.md` — Summary of all shipped milestones

---
*Last updated: 2026-01-30 (v2.0.0 milestone complete)*
