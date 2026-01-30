# SharpDicom

## What This Is

A complete DICOM toolkit for .NET built from scratch — library, CLI tools, and migration tooling. Designed as a cleaner, faster replacement for fo-dicom with streaming-first architecture. Now includes full networking stack, image codecs, and de-identification.

## Core Value

Efficient streaming of DICOM data between network, disk, and document databases without materializing entire datasets in memory.

## Current State

**Shipped v2.0.0** — Network, Codecs & De-identification (2026-01-30)

- 47,877 lines of C#
- 3870 tests passing
- Multi-target: netstandard2.0, net8.0, net9.0, net10.0
- Trim/AOT compatible

## Requirements

### Validated

- ✓ Parse DICOM Part 10 files with streaming support — v1.0
- ✓ Write DICOM Part 10 files — v1.0
- ✓ Source-generated DICOM dictionary from NEMA XML — v1.0
- ✓ Multi-target: netstandard2.0, net8.0, net9.0 — v1.0
- ✓ Trim/AOT compatible (no reflection) — v1.0
- ✓ Full DICOM networking (C-ECHO, C-STORE, C-FIND, C-MOVE, C-GET) — v2.0
- ✓ JPEG and JPEG 2000 codecs (pure C# and native accelerated) — v2.0
- ✓ PS3.15 de-identification with UID remapping and date shifting — v2.0

### Active

- [ ] DICOM-Web APIs (WADO-RS, STOW-RS, QIDO-RS)
- [ ] MongoDB/BSON serialization
- [ ] CLI tools (dcmdump, storescu, findscu equivalents)
- [ ] TLS support for networking
- [ ] Transfer syntax negotiation with transcoding

### Out of Scope

- fo-dicom API compatibility in core library — clean break, best design wins
- DIMSE-N services — Normalized objects are <5% of use cases
- C-FIND/C-MOVE SCP — Most users are SCU
- Burned-in PHI detection with OCR — Too complex, risk detection only
- Federation daemon — Future vision

## Context

**Shipped versions:**
- v1.0.0 — Core DICOM Toolkit (Phases 1-9)
- v2.0.0 — Network, Codecs & De-identification (Phases 10-14)

**Migration targets**: RdmpDicom, SmiServices, nccid, dcm2csv

**Architecture pattern**: Metadata → MongoDB, pixels → disk (used in SMI/SmiServices)

**Tech stack:**
- Pure C# with source generators
- Zig for native codec cross-compilation
- SQLite for UID remapping persistence
- System.Text.Json for configuration

## Constraints

- **Dependencies**: Minimal — System.Memory for netstandard2.0 Span support, optional SQLite for de-id persistence
- **Compatibility**: Trim-safe, AOT-ready, no reflection
- **Targets**: netstandard2.0 (broad compat), net8.0 (LTS), net9.0, net10.0 (latest features)
- **Design**: Span<T>-first, streaming, minimal allocations

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Clean break from fo-dicom API | Best design wins, migration via tooling | ✓ Good |
| Source generator for dictionary | Trim/AOT compatibility, no reflection | ✓ Good |
| Multi-target from start | Broad compatibility required for migration | ✓ Good |
| DicomStreamReader as ref struct | Zero-copy Span<T> parsing | ✓ Good |
| PduReader/PduWriter as ref structs | Zero-copy PDU parsing | ✓ Good |
| 13-state association state machine | Matches PS3.8 exactly | ✓ Good |
| IAsyncEnumerable for query/retrieve | Streaming results, memory efficient | ✓ Good |
| Pure C# codecs with explicit registration | AOT compatible, no ModuleInitializer | ✓ Good |
| Zig for cross-compilation | Single toolchain, 6 targets | ✓ Good |
| System.Text.Json for de-id config | AOT-friendly, built-in | ✓ Good |
| $extends inheritance for config | Flexible composition | ✓ Good |

---
*Last updated: 2026-01-30 after v2.0.0 milestone*
