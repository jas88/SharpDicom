# SharpDicom

## What This Is

A complete DICOM toolkit for .NET built from scratch — library, CLI tools, and migration tooling. Designed as a cleaner, faster replacement for fo-dicom with streaming-first architecture optimized for workflows where metadata goes to MongoDB and pixel data stays on disk.

## Core Value

Efficient streaming of DICOM data between network, disk, and document databases without materializing entire datasets in memory.

## Requirements

### Validated

- [x] Parse DICOM Part 10 files with streaming support — v1.0
- [x] Write DICOM Part 10 files — v1.0
- [x] Source-generated DICOM dictionary from NEMA XML — v1.0
- [x] Multi-target: netstandard2.0, net8.0, net9.0 — v1.0
- [x] Trim/AOT compatible (no reflection) — v1.0
- [x] RLE Lossless codec with SIMD optimization — v1.0
- [x] Validation framework (Strict/Lenient/Permissive) — v1.0
- [x] Private tag support with vendor dictionaries — v1.0
- [x] Character encoding (ISO-IR 6 through UTF-8) — v1.0
- [x] DICOM networking (C-ECHO, C-STORE, C-FIND, C-MOVE, C-GET) — v2.0
- [x] Pure C# image codecs (JPEG Baseline, JPEG Lossless, JPEG 2000) — v2.0
- [x] Native codecs package (libjpeg-turbo, OpenJPEG, CharLS) — v2.0
- [x] De-identification (PS3.15 Basic Profile) — v2.0
- [x] Zero-copy PDU infrastructure — v2.0
- [x] JPEG-LS and HTJ2K codec infrastructure — v2.0
- [x] Modality Worklist (MWL) SCU support — v2.0

### Active

- [ ] Complete managed JPEG-LS codec (full ITU-T T.87) — v3.0
- [ ] Complete managed HTJ2K codec — v3.0
- [ ] TLS networking support — v3.0
- [ ] Basic CLI tool (dcmdump equivalent)

### Out of Scope

- fo-dicom API compatibility in core library — clean break, best design wins
- MongoDB/BSON serialization — v4+
- Full CLI suite (storescu, findscu, etc.) — v4+
- Federation daemon — future vision

## Context

**Migration targets**: RdmpDicom, SmiServices, nccid, dcm2csv (dcm2csv first)

**Architecture pattern**: Metadata → MongoDB, pixels → disk (used in SMI/SmiServices)

**Existing design**: CLAUDE.md contains extensive architectural decisions — use as starting point, not locked specification.

**Migration tooling** (build alongside core):
- SharpDicom.FoDicom.Compat — adapter layer mimicking fo-dicom API
- SharpDicom.Analyzers — Roslyn analyzer flagging fo-dicom patterns

**Long-term vision**: PACS federation daemon (Usenet-style) with push for redundancy, pull for discovery.

## Constraints

- **Dependencies**: Minimal — only System.Memory for netstandard2.0 Span support
- **Compatibility**: Trim-safe, AOT-ready, no reflection
- **Targets**: netstandard2.0 (broad compat), net8.0 (LTS), net9.0 (latest features)
- **Design**: Span<T>-first, streaming, minimal allocations

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Clean break from fo-dicom API | Best design wins, migration via tooling | ✓ Good (v1.0) |
| Source generator for dictionary | Trim/AOT compatibility, no reflection | ✓ Good (v1.0) |
| Multi-target from start | Broad compatibility required for migration | ✓ Good (v1.0) |
| Two-layer reader (ref struct + async) | Zero-allocation + streaming | ✓ Good (v1.0) |
| Span<T>-first design | Minimize allocations in hot paths | ✓ Good (v1.0) |
| FrozenDictionary on .NET 8+ | 40-50% faster dictionary lookups | ✓ Good (v1.0) |
| 13-state association machine | Match PS3.8 Section 9.2 | ✓ Good (v2.0) |
| IAsyncEnumerable for Q/R | Memory-efficient streaming | ✓ Good (v2.0) |
| Zig cross-compilation | Single toolchain for 6 targets | ✓ Good (v2.0) |
| UUID-derived UIDs (2.25.xxx) | No registered root needed | ✓ Good (v2.0) |

## Context

Shipped v2.0 with ~89,000 LOC C#.
Tech stack: .NET multi-target (netstandard2.0, net8.0, net9.0, net10.0), Roslyn source generators.
3660 tests at v2.0 release.

---
*Last updated: 2026-02-02 after v2.0 milestone*
