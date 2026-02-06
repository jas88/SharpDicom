# Phase 26: Migration Tooling - Context

**Gathered:** 2026-02-06
**Status:** Ready for planning

<domain>
## Phase Boundary

Provide a migration path from fo-dicom to SharpDicom. Two deliverables:

1. **SharpDicom.FoDicom.Compat** — Adapter layer replicating fo-dicom's public API surface, delegating to SharpDicom internally. Two separate packages for fo-dicom 4.x and 5.x.
2. **SharpDicom.Analyzers** — Roslyn analyzer detecting fo-dicom patterns and providing automatic code fix providers for migration.

Phase is gated on dcm2csv and nccid compiling and passing tests against the compat layer.

</domain>

<decisions>
## Implementation Decisions

### Compat Layer API Surface
- **Broad API mirror** — replicate most of fo-dicom's public API surface, not just targeted wrappers
- **Own namespace** — types live in SharpDicom.FoDicom.Compat (not fo-dicom's original namespaces), but internally mirror fo-dicom namespaces (FoDicom4 uses `Dicom`, FoDicom5 uses `FellowOakDicom`)
- **Both 4.x and 5.x** — separate NuGet packages: SharpDicom.FoDicom4.Compat and SharpDicom.FoDicom5.Compat
- **No fo-dicom dependency** — standalone packages that replicate fo-dicom API signatures without referencing fo-dicom NuGet
- **Thin wrappers (composition)** — new classes wrapping SharpDicom types internally, fo-dicom API on the outside
- **Expose via .Unwrap()** — compat wrappers expose underlying SharpDicom object for gradual migration
- **Compile-time errors for unsupported features** — `[Obsolete("Not supported in SharpDicom", error: true)]` on APIs that SharpDicom doesn't implement

### Analyzer Detection & Fixes
- **Two migration modes** — "quick migration" (fo-dicom → compat) and "full migration" (fo-dicom → native SharpDicom), user selects via .editorconfig
- **Two-step analyzer** — also detects compat layer usage and suggests native SharpDicom (for second migration step: compat → native)
- **Full code fix providers** — Roslyn CodeFixProvider with auto-fix lightbulb actions, not just diagnostics
- **Configurable severity** — ship with Warning default, users override to Error or Info via .editorconfig

### Migration Target Validation
- **dcm2csv + nccid as phase gates** — phase isn't complete until both compile and pass tests against compat layer
- **dcm2csv uses fo-dicom 5.x** — primary validation target for FoDicom5.Compat package
- **Separate repos need cloning** — dcm2csv and nccid are on GitHub (jas88 forks), need cloning for validation
- **SmiServices and RdmpDicom deferred** — too complex for this phase, validated in future work

### Behavioral Differences
- **Match fo-dicom behavior** — compat layer faithfully replicates fo-dicom behavior for maximum drop-in compatibility (e.g., eager loading, synchronous patterns)
- **Replicate fo-dicom exception types** — define matching exception classes (DicomDataException, DicomNetworkException, etc.) in compat namespace
- **Mirror fo-dicom namespaces internally** — FoDicom4 package uses `Dicom` namespace, FoDicom5 uses `FellowOakDicom` for maximum drop-in compatibility
- **Typed dispatch + reflection fallback for Get<T>** — fast path for common types (string, int, DateTime, DicomUID), reflection fallback for exotic types

### Claude's Discretion
- Exact set of fo-dicom types to replicate (based on research of actual API surface)
- Internal architecture of the analyzer (syntax vs semantic analysis)
- Test structure and organization
- Build/CI integration details

</decisions>

<specifics>
## Specific Ideas

- Two-step migration path: fo-dicom → compat (quick, compile-fix) → native SharpDicom (thorough, performance gains)
- .Unwrap() enables gradual per-method migration within a codebase
- Analyzer in .editorconfig means CI can enforce migration progress (set to Error to block fo-dicom usage)

</specifics>

<deferred>
## Deferred Ideas

- SmiServices migration — complex, depends on BSON serialization (Phase 29) and more compat surface
- RdmpDicom migration — complex, depends on extensive DicomTypeTranslation compat
- Migration guide documentation — could be a separate docs effort
- Benchmark comparisons (fo-dicom vs SharpDicom via compat vs SharpDicom native) — useful but not blocking

</deferred>

---

*Phase: 26-migration-tooling*
*Context gathered: 2026-02-06*
