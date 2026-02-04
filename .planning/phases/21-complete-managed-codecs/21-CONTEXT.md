# Phase 21: Complete Managed Codecs - Context

**Gathered:** 2026-02-03
**Status:** Ready for planning

<domain>
## Phase Boundary

Complete the pure C# JPEG-LS and HTJ2K codec implementations that were stubbed in v2.0. Infrastructure exists; this phase implements the actual encoding/decoding algorithms to full standard compliance.

</domain>

<decisions>
## Implementation Decisions

### JPEG-LS modes
- Implement all 8 predictors from ITU-T T.87 for full compliance
- Full near-lossless support (NEAR values 1-255) for configurable quality/size tradeoff
- Support all three interleaving modes (line, sample, none) for multi-component images
- Implement both Part 1 (core) and Part 2 (extensions including arithmetic coding)

### HTJ2K configuration
- Quality layers: Claude's discretion based on DICOM standard requirements
- Resolution levels: Configurable 1-6 levels, caller specifies based on image size
- Tiling: Configurable tile sizes for parallel decode of large images
- Both lossless (reversible 5/3 DWT) and lossy (irreversible 9/7 DWT) required

### Performance targets
- Acceptable speed: Within 10x of native codecs (libjpeg-turbo, OpenJPEG)
- Full SIMD optimization using Vector128/256 for hot paths (DWT, entropy coding)
- Auto-parallel: Automatically use multiple cores for large images
- Span-based zero-copy memory strategy with stackalloc where safe

### Error handling
- Corrupt data: Configurable strict mode (fail fast) vs lenient mode (best effort)
- Truncation: Configurable - option to return partial decode or fail with position
- Validation: Match reference implementation behavior for accept/reject
- Error messages: Both levels - inner exception with technical detail, outer with user message

### Claude's Discretion
- HTJ2K quality layer count (single vs multiple)
- Specific SIMD instruction selection per platform
- Threshold for auto-parallelism activation
- Default tile size recommendations

</decisions>

<specifics>
## Specific Ideas

- Part 2 JPEG-LS support is unusual but requested for full standard compliance
- Performance target of 10x native is realistic for pure C# with SIMD
- Auto-parallel should "just work" without caller configuration for large images
- Configurable error handling matches the existing validation framework pattern (Strict/Lenient/Permissive)

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 21-complete-managed-codecs*
*Context gathered: 2026-02-03*
