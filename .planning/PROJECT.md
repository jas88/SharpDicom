# SharpDicom

## What This Is

A complete DICOM toolkit for .NET built from scratch — library, CLI tools, and migration tooling. Designed as a cleaner, faster replacement for fo-dicom with streaming-first architecture optimized for workflows where metadata goes to MongoDB and pixel data stays on disk.

## Core Value

Efficient streaming of DICOM data between network, disk, and document databases without materializing entire datasets in memory.

## Requirements

### Validated

(None yet — ship to validate)

### Active

- [ ] Parse DICOM Part 10 files with streaming support
- [ ] Write DICOM Part 10 files
- [ ] Source-generated DICOM dictionary from NEMA XML
- [ ] Multi-target: netstandard2.0, net8.0, net9.0
- [ ] Trim/AOT compatible (no reflection)
- [ ] Basic CLI tool (dcmdump equivalent)

### Out of Scope

- fo-dicom API compatibility in core library — clean break, best design wins
- MongoDB/BSON serialization — v2
- Networking (C-STORE, C-FIND, C-MOVE) — v2
- Full CLI suite (storescu, findscu, etc.) — v2
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
| Clean break from fo-dicom API | Best design wins, migration via tooling | — Pending |
| Source generator for dictionary | Trim/AOT compatibility, no reflection | — Pending |
| Multi-target from start | Broad compatibility required for migration | — Pending |
| Build migration tools alongside | Validate migration path as features develop | — Pending |

---
*Last updated: 2025-01-26 after initialization*
