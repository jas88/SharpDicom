# Phase 3: Implicit VR & Sequences - Context

**Gathered:** 2026-01-27
**Status:** Ready for planning

<domain>
## Phase Boundary

Handle real-world DICOM files that use Implicit VR Little Endian transfer syntax and contain sequences (nested datasets). Extends Phase 2 parser to support VR lookup from dictionary and sequence parsing with proper delimiter handling. Pixel data handling is Phase 5.

</domain>

<decisions>
## Implementation Decisions

### Multi-VR Tag Resolution

- **Pixel Data (7FE0,0010):** Use BitsAllocated context - OW if BitsAllocated > 8, OB otherwise (DICOM standard)
- **US/SS ambiguous tags:** Use PixelRepresentation context - US if PixelRepresentation = 0 (unsigned), SS if = 1 (signed)
- **LUT Data tags:** Use LUT descriptor - OW if number of entries > 256, US otherwise
- **Missing context tags:** Mode-dependent - strict mode throws, lenient mode uses safe defaults (BitsAllocated=16, PixelRepresentation=0)
- **VR resolution logging:** Only in verbose mode (DicomReaderOptions.Verbose flag)
- **Manual VR override:** Expose ResolveVR method on dataset for callers to force VR resolution after parsing
- **VR storage:** Store resolved VR only - no need to track both original and resolved
- **Private tags default:** Map to UN (Unknown) when VR cannot be determined
- **Context caching:** Cache key context values (BitsAllocated, PixelRepresentation) as typed properties on DicomDataset
- **Out-of-order context:** Buffer elements with deferred VR, resolve when context arrives
- **Missing context (deferred):** Keep as UN if context tag never appears
- **UN element access:** Best effort - typed accessors (GetInt32, GetString) attempt to parse UN raw bytes
- **IsVRDeferred API:** Expose IsVRDeferred property on elements
- **Explicit VR conflict:** Trust the declared VR in the file, not the calculated context
- **64-bit VRs:** Handle OV/SV/UV (DICOM 2020) in this phase

### Malformed Sequence Handling

- **Missing delimiter:** Mode-dependent - strict throws DicomDataException, lenient attempts heuristic recovery
- **Bad nesting:** Mode-dependent - strict fails, lenient skips malformed item
- **Truncated files:** Mode-dependent - strict throws, lenient returns partial dataset
- **Length mismatch:** Strict enforces match between declared and actual content; lenient accepts actual content
- **Error tracking:** Expose IsTruncated and ParsingWarnings properties on DicomDataset
- **Empty sequences:** Return empty collection (never null) for DicomSequence.Items
- **Unknown tags in sequence:** Mode-dependent - strict fails on unknown, lenient includes them
- **Parent reference:** Each sequence item has Parent property referencing parent dataset
- **Delimiter order errors:** Mode-dependent - strict fails, lenient attempts recovery
- **Error collection:** Mode-dependent - strict stops at first error, lenient collects all
- **Error callback:** No custom callback - rely on Strict/Lenient/Permissive modes
- **Debug info:** ParsingWarning includes hex dump of problematic bytes for debugging

### Nesting Depth Limits

- **Max depth:** Configurable via DicomReaderOptions.MaxSequenceDepth
- **Default max depth:** 128 levels
- **Depth exceeded:** Mode-dependent - strict throws, lenient truncates and warns
- **Implementation:** Use explicit stack (heap-allocated), not recursion, to avoid stack overflow
- **Max total items:** Configurable via DicomReaderOptions.MaxTotalItems, default 100,000
- **Depth API:** Expose CurrentDepth property during streaming reads

### Deferred VR Resolution

- **Resolution timing:** On first access (lazy resolution)
- **Element type:** Same element type with IsVRDeferred flag property (not wrapper type)
- **Thread safety:** Resolution is thread-safe using locking/Lazy<T>
- **Streaming behavior:** Resolve deferred elements before yielding in IAsyncEnumerable
- **Buffer limit:** Configurable MaxDeferredBufferSize for streaming
- **Buffer exceeded:** Mode-dependent - strict throws, lenient applies defaults
- **Look-ahead:** Yes - limited look-ahead in stream to find context tags
- **Context inheritance:** Nested sequence items inherit context (BitsAllocated, etc.) from parent dataset

### Claude's Discretion

- Exact look-ahead buffer size default
- Specific heuristics for recovery from malformed sequences
- Memory allocation patterns for explicit stack
- Verbose logging format

</decisions>

<specifics>
## Specific Ideas

- Follow DICOM standard recommendations for VR resolution (PS3.5)
- Mode-dependent behavior should consistently favor "strict fails, lenient recovers/continues"
- Explicit stack avoids .NET stack limitations for deeply nested sequences
- Thread-safe lazy resolution enables concurrent dataset access

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 03-implicit-vr-sequences*
*Context gathered: 2026-01-27*
