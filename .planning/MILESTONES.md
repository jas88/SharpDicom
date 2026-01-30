# Project Milestones: SharpDicom

## v2.0.0 Network, Codecs & De-identification (Shipped: 2026-01-30)

**Delivered:** Full DICOM networking stack, pluggable image codecs (pure C# and native accelerated), and PS3.15-compliant de-identification.

**Phases completed:** 10-14 (38 plans total)

**Key accomplishments:**

- Full DICOM networking stack — PDU parsing, association state machine, C-ECHO/C-STORE/C-FIND/C-MOVE/C-GET SCU/SCP
- Pure C# image codecs — JPEG Baseline, JPEG Lossless, JPEG 2000 Lossless/Lossy with AOT compatibility
- Native codec acceleration — libjpeg-turbo, OpenJPEG, CharLS, FFmpeg wrappers via Zig cross-compilation
- GPU support — nvJPEG2000 integration with automatic CPU fallback
- PS3.15 de-identification — Source-generated action tables, UID remapping, date shifting, pixel redaction
- Batch processing — Parallel directory de-identification with progress reporting

**Stats:**

- 47,877 lines of C#
- 5 phases, 38 plans executed
- 3 days from start to ship (2026-01-28 → 2026-01-30)
- 3870 tests passing (126 skipped for external services)

**Git range:** `feat(10-01)` → `docs: update STATE.md`

**What's next:** v3.0 could add: DICOM-Web APIs, MongoDB serialization, CLI tools, federation daemon

---

## v1.0.0 Core DICOM Toolkit (Shipped: 2026-01-27)

**Delivered:** Production-ready DICOM file I/O with streaming architecture.

**Phases completed:** 1-9 (30 plans total)

**Key accomplishments:**

- Core data model with source-generated DICOM dictionary (4000+ tags, 1000+ UIDs)
- Streaming file reading with Span<T>-first architecture
- Implicit VR and sequence parsing with depth guards
- Character encoding (UTF-8, ISO 8859-x, CJK, ISO 2022)
- Pixel data with lazy loading and fragment support
- Private tag support with vendor dictionaries (9268 tags)
- File writing with sequence support (both length modes)
- Validation framework with Strict/Lenient/Permissive profiles
- RLE codec with SIMD optimization

**Stats:**

- 9 phases, 30 plans executed
- Multi-target: netstandard2.0, net8.0, net9.0, net10.0
- Trim/AOT compatible (no reflection)

**Git range:** Initial commit → v1.0 completion

**What's next:** v2.0 — Network, Codecs & De-identification

---
